'''
Entry points for the engine oracle tests. Each calls an original ScrapeEngine method with a stand-in engine whose
config holds the test settings, over Python ComicBooks built the way scrapeengine builds them.
'''
from scrapeengine import ScrapeEngine
import cvs_book_oracle

class _Engine(object):
	def __init__(self, settings):
		self.config = cvs_book_oracle._Settings(settings)

def sort_books(crbooks, fast_rescrape, ignore_folders):
	''' The positions of crbooks in the order __sort_books puts them. '''
	settings = {'fast_rescrape_b': fast_rescrape, 'ignore_folders_b': ignore_folders}
	books = []
	for i, crbook in enumerate(crbooks):
		book = cvs_book_oracle._attached(crbook, settings)
		book.oracle_position = i
		books.append(book)
	ordered = ScrapeEngine._ScrapeEngine__sort_books.im_func(_Engine(settings), books)
	return [book.oracle_position for book in ordered]

def choose_issue(issues, number, force):
	'''
	__choose_issue_ref over an issue list that is already loaded, so no request is made. Returns ['ok', key] when it
	picks an issue without asking, or ['dialog', hint key or None] when it shows the IssueForm, which is stubbed.
	'''
	import scrapeengine
	from dbmodels import IssueRef, SeriesRef
	shown = []
	class _IssueForm(object):
		def __init__(self, engine, hint, refs, series):
			shown.append(hint)
		def __enter__(self):
			return self
		def __exit__(self, *args):
			return False
		def show_form(self):
			return None
	class _Book(object):
		issue_num_s = number
	refs = set(IssueRef(num, key, '', None) for key, num in issues)
	saved = scrapeengine.IssueForm
	scrapeengine.IssueForm = _IssueForm
	try:
		result = ScrapeEngine._ScrapeEngine__choose_issue_ref.im_func(
			_Engine({}), _Book(), SeriesRef(1, 'Series', -1, '', len(refs), None), refs, force)
	finally:
		scrapeengine.IssueForm = saved
	if shown:
		return ['dialog', shown[0].issue_key if shown[0] else None]
	return ['ok', result.get_ref().issue_key]

def run_scrape(crbooks, s):
	'''
	The original __scrape loop and __scrape_book over the given ComicRack books. Only the edges are replaced: the
	forms answer from the scripted queues in s, db and the auto-matcher answer from the scenario tables in s, and
	ComicBook.update and skip_forever record the write instead of making it. Returns the events in order.
	'''
	import scrapeengine
	from System.IO import Path
	from dbmodels import IssueRef, SeriesRef
	from searchform import SearchFormResult
	from seriesform import SeriesFormResult
	from issueform import IssueFormResult
	from comicbook import ComicBook

	events = []
	recorded = set()
	series = dict((int(k), SeriesRef(int(k), v[0], v[1], '', v[2], None)) for k, v in s['series'].items())
	issues = dict((int(k), [IssueRef(num, key, '', None) for key, num in v]) for k, v in s['issues'].items())
	issue_by_key = dict((ref.issue_key, ref) for refs in issues.values() for ref in refs)
	fails = dict((int(k), v) for k, v in s['fail'].items())
	answers = {'search': list(s['answers_search']), 'series': list(s['answers_series']), 'issue': list(s['answers_issue'])}
	positions = dict((id(book), i) for i, book in enumerate(crbooks))

	def next_answer(kind):
		queue = answers[kind]
		return queue.pop(0) if queue else 'CANCEL'

	class _Config(object):
		def __init__(self):
			self.api_key_s = 'key'
			self.welcome_dialog_b = False
			self.summary_dialog_b = False
			self.max_search_results_n = 100
			self.fast_rescrape_b = s['fast']
			self.autochoose_series_b = s['auto']
			self.confirm_issue_b = s['confirm']
			self.update_rating_b = False
			self.scrape_delay_n = 0
			self.ignore_folders_b = False
			self.alt_search_regex_s = ''
			self.session_data_map = {}
			self.ignored_searchterms_sl = []
			self.ignored_publishers_sl = []
			self.ignored_before_year_n = -1
			self.ignored_after_year_n = -1
			self.never_ignore_threshold_n = 0
		def load_defaults(self):
			pass

	class _Issue(object):
		def __init__(self, key):
			self.issue_key = key

	class _Db(object):
		def initialize(self, **kwargs):
			pass
		def check_magic_file(self, path):
			key = s['magic'].get(Path.GetDirectoryName(path)) if path else None
			return series[key] if key else None
		def query_series_refs(self, terms, ignored, callback):
			return set(series[key] for key in s['search'].get(terms, []))
		def query_issue_refs(self, series_ref, callback):
			return set(issues.get(series_ref.series_key, []))
		def query_issue_ref(self, series_ref, number):
			key = s['find'].get('%d|%s' % (series_ref.series_key, number))
			return issue_by_key[key] if key else None
		def query_issue(self, issue_ref, rating):
			key = issue_ref.issue_key
			if fails.get(key, 0) > 0:
				fails[key] -= 1
				raise Exception('network')
			return _Issue(key)

	class _DbUtils(object):
		def filter_series_refs(self, refs, *args):
			return refs

	class _AutoMatcher(object):
		def find_series_ref(self, book, config):
			key = s['automatch'].get(str(book.oracle_position), 0)
			return series[key] if key else None

	class _Book(ComicBook):
		def __init__(self, crbook, scraper):
			ComicBook.__init__(self, crbook, scraper)
			self.oracle_position = positions[id(crbook)]
		def update(self, issue):
			events.append('write %d %s' % (self.oracle_position, issue.issue_key))
		def skip_forever(self):
			events.append('skipforever %d' % self.oracle_position)

	class _Form(object):
		def __enter__(self):
			return self
		def __exit__(self, *args):
			return False

	class _SearchForm(_Form):
		def __init__(self, engine, initial, failed):
			events.append('search %s|%s' % (initial or '', failed or ''))
		def show_form(self):
			answer = next_answer('search')
			return SearchFormResult('SEARCH', answer[7:]) if answer.startswith('SEARCH:') else SearchFormResult(answer)

	class _SeriesForm(_Form):
		def __init__(self, engine, book, refs, terms):
			self.refs = sorted(refs, key=lambda ref: ref.series_key)
			events.append('series %s [%s]' % (terms, ' '.join(str(ref.series_key) for ref in self.refs)))
		def show_form(self):
			answer = next_answer('series')
			if ':' in answer:
				kind, k = answer.split(':')
				return SeriesFormResult(kind, self.refs[int(k) % len(self.refs)])
			return SeriesFormResult(answer)

	class _IssueForm(_Form):
		def __init__(self, engine, hint, refs, series_ref):
			self.refs = sorted(refs, key=lambda ref: ref.issue_key)
			events.append('issue %s %s [%s]' % (series_ref.series_key, hint.issue_key if hint else '-',
				' '.join(str(ref.issue_key) for ref in self.refs)))
		def show_form(self):
			answer = next_answer('issue')
			if answer == 'NONE':
				return None
			if ':' in answer:
				return IssueFormResult('OK', self.refs[int(answer.split(':')[1]) % len(self.refs)])
			return IssueFormResult(answer)

	class _ProgressBarForm(_Form):
		def __init__(self, *args):
			pass

	class _MessageBox(object):
		@staticmethod
		def Show(*args):
			events.append('noissues')

	class _I18n(object):
		def get(self, key):
			return key + ' {0}'

	class _ComicForm(object):
		@staticmethod
		def show_threadsafe(engine):
			return None

	class _MainWindow(object):
		def Activate(self):
			pass

	class _ComicRack(object):
		MainWindow = _MainWindow()

	class _MatchScore(object):
		def record_choice(self, series_ref):
			recorded.add(str(series_ref.series_key))

	replaced = {'Configuration': lambda: _Config(), 'db': _Db(), 'dbutils': _DbUtils(), 'automatcher': _AutoMatcher(),
		'ComicBook': _Book, 'ComicForm': _ComicForm, 'SearchForm': _SearchForm, 'SeriesForm': _SeriesForm,
		'IssueForm': _IssueForm, 'ProgressBarForm': _ProgressBarForm, 'MessageBox': _MessageBox, 'i18n': _I18n()}
	saved = dict((name, getattr(scrapeengine, name)) for name in replaced)
	engine = ScrapeEngine.__new__(ScrapeEngine)
	engine.comicrack = _ComicRack()
	engine.start_scrape_listeners = [lambda book, remaining: events.append('start %d %d' % (book.oracle_position, remaining))]
	engine.cancel_listeners = []
	engine._ScrapeEngine__cancelled_b = False
	engine._ScrapeEngine__status = [0, 0]
	engine._ScrapeEngine__matchscore = _MatchScore()
	engine._ScrapeEngine__wait_until_ready = lambda: events.append('wait')
	try:
		for name, value in replaced.items():
			setattr(scrapeengine, name, value)
		try:
			engine._ScrapeEngine__scrape(list(crbooks))
		except Exception as e:
			events.append('error')
	finally:
		for name, value in saved.items():
			setattr(scrapeengine, name, value)
	status = engine._ScrapeEngine__status
	events.append('end %d %d %s' % (status[0], status[1], engine._ScrapeEngine__cancelled_b))
	events.append('recorded ' + ' '.join(sorted(recorded)))
	return events
