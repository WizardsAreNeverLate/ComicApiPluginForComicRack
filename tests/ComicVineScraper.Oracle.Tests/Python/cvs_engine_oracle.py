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
