# Adapters that let the oracle tests reach Python code that is not a plain module function.
# Each one builds the inputs the original code expects, calls it, and returns plain values.
import utils
import dbutils
import dbmodels
import matchscore
import cvdb
import db


class _Obj(object):
   pass


def lower(s):
   return s.lower()


def natural_keys_equal(a, b):
   return utils.natural_key(a) == utils.natural_key(b)


def natural_key_has_nan(s):
   return any(isinstance(x, float) and x != x for x in utils.natural_key(s))


def strip_ignored_terms(terms, ignored):
   ''' db.query_series_refs, with the Comic Vine query replaced by a recorder of the terms it receives. '''
   captured = []
   def record(search_terms_s, callback_function):
      captured.append(search_terms_s)
      return set()
   original = cvdb._query_series_refs
   cvdb._query_series_refs = record
   try:
      setattr(db, '__series_ref_cache', {})
      db.query_series_refs(terms, ignored)
   finally:
      cvdb._query_series_refs = original
   return captured[0]


def issue_summary(description):
   dom = _Obj()
   dom.results = _Obj()
   dom.results.description = description
   issue = _Obj()
   issue.summary_s = None
   getattr(cvdb, '__issue_parse_summary')(issue, dom)
   return issue.summary_s


def filter_series_keys(rows, ignored, before, after, threshold):
   refs = [dbmodels.SeriesRef(*row) for row in rows]
   kept = dbutils.filter_series_refs(refs, ignored, before, after, threshold)
   return sorted(r.series_key for r in kept)


def match_score(book_row, series_row, prior_keys):
   book = _Obj()
   book.series_s, book.format_s, book.issue_num_s, book.pub_year_n, book.rel_year_n = book_row
   score = matchscore.MatchScore()
   score._MatchScore__prior_series_sl = set(prior_keys)
   return score.compute_n(book, dbmodels.SeriesRef(*series_row))
