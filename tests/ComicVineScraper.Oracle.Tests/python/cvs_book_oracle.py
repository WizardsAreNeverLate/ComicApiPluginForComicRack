'''
Entry points for the book oracle tests. Each builds a Python ComicBook the way scrapeengine does, with a stand-in
scraper whose config holds the given settings, and returns plain values.
'''
import time
import comicbook
from comicbook import ComicBook
from bookdata import BookData
from dbmodels import Issue, IssueRef

class _Settings(object):
	def __init__(self, values):
		self.__dict__.update(values)
		self.session_data_map = {}
		self.alt_search_regex_s = ''

class _Scraper(object):
	def __init__(self, settings):
		self.config = _Settings(settings)
		self.comicrack = None

_now = [None]

def _strftime(fmt):
	# deviations.md #10: the port writes the note time as invariant HH:mm:ss, not %X
	return time.strftime(fmt.replace('%X', '%H:%M:%S'), _now[0])

def _detached(settings, now=None, fields=None):
	''' A ComicBook over a plain BookData, for the methods that never touch ComicRack. '''
	_now[0] = tuple(now) + (0, 1, -1) if now else None
	comicbook.strftime = _strftime
	book = ComicBook.__new__(ComicBook)
	book._ComicBook__scraper = _Scraper(settings)
	data = BookData()
	for name, value in (fields or {}).items():
		setattr(data, name, value)
	book._ComicBook__bookdata = data
	return book

def _attached(crbook, settings, now=None):
	_now[0] = tuple(now) + (0, 1, -1) if now else None
	comicbook.strftime = _strftime
	return ComicBook(crbook, _Scraper(settings))

def _marker(book):
	try:
		if book.skip_b:
			return ['skip']
		ref = book.issue_ref
		return ['issue', str(ref.issue_key)] if ref else ['none']
	except Exception as e:
		return ['error', repr(e)]

def find_marker(tags, notes, issue_key):
	return _marker(_detached({}, fields={'tags_sl': tags, 'notes_s': notes, 'issue_key_s': issue_key}))

def add_key_to_tags(tags, issue_key):
	try:
		return list(_detached({})._ComicBook__add_key_to_tags(tags, issue_key))
	except Exception as e:
		return 'error ' + repr(e)

def add_key_to_notes(notes, issue_key, now):
	try:
		return _detached({'note_scrape_date_b': now is not None}, now)._ComicBook__add_key_to_notes(notes, issue_key)
	except Exception as e:
		return 'error ' + repr(e)

def unique_series(series, format, volume, path, ignore_folders):
	book = _detached({'ignore_folders_b': ignore_folders},
		fields={'series_s': series, 'format_s': format, 'volume_year_n': volume, 'path_s': path})
	return book.unique_series_s

def facts(crbook, ignore_folders):
	''' What ComicBook derives from a ComicRack book: the name fields, the stored marker, the series key. '''
	book = _attached(crbook, {'ignore_folders_b': ignore_folders})
	sref = None
	try:
		ref = book._ComicBook__extract_series_ref()
		sref = str(ref.series_key) if ref else ''
	except Exception as e:
		sref = 'error ' + repr(e)
	return [book.series_s, book.issue_num_s, book.pub_year_n, _marker(book), sref, book.unique_series_s]

def _issue(values):
	issue = Issue(IssueRef(values['issue_num_s'], values['issue_key'], '', None))
	for name, value in values.items():
		setattr(issue, name, value)
	return issue

def issue_values(values):
	''' The issue fields after dbmodels.Issue's setters have cleaned them. '''
	issue = _issue(values)
	return dict((name, getattr(issue, name)) for name in values.keys())

def update(crbook, settings, values, now):
	_attached(crbook, settings, now).update(_issue(values))

def skip_forever(crbook, settings, now):
	_attached(crbook, settings, now).skip_forever()

def strftime(fmt, values):
	return time.strftime(fmt, tuple(values))

def strftime_in(locale_name, fmt, values):
	''' time.strftime with LC_TIME set to the given .NET culture name, or an error text. '''
	import _locale as locale
	old = locale.setlocale(locale.LC_TIME)
	try:
		locale.setlocale(locale.LC_TIME, locale_name)
		return time.strftime(fmt, tuple(values))
	except Exception as e:
		return 'error ' + repr(e)
	finally:
		locale.setlocale(locale.LC_TIME, old)
