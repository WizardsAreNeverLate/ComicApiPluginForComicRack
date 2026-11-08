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
