'''
Runs the original db.py and cvdb.py against recorded responses. Only the transport is replaced: the
page download (utils.get_html_string), the request throttle, and the retry sleep. serve(url) returns a
response with Status and Body, or None when there is no fixture.
'''
import clr
clr.AddReference('System.Web')
from System.IO import StringWriter, IOException
from System.Net import WebException
from System.Web import HttpUtility

import utils
import cvconnection
import cvdb
import db
import dbutils
from dbmodels import SeriesRef, IssueRef

_requested = []


class _NoSleep(object):
   def __init__(self, start):
      pass

   def Start(self):
      pass

   def Join(self):
      pass


def html_decode(page):
   writer = StringWriter()
   HttpUtility.HtmlDecode(page, writer)
   return writer.ToString()


def _install(serve, max_results):
   def get_html_string(url):
      _requested.append(url)
      response = serve(url)
      if response is None:
         raise IOException("no fixture for " + url)
      if int(response.Status) != 200:
         raise WebException("server response code " + str(int(response.Status)))
      return html_decode(response.Body)
   utils.get_html_string = get_html_string
   cvconnection.wait_until_ready = lambda: None
   cvconnection.Thread = _NoSleep
   del _requested[:]
   db.initialize(cv_apikey="oracle", cv_maxresults=max_results)


def _series(ref):
   return [int(ref.series_key), ref.series_name_s, ref.volume_year_n, ref.publisher_s,
      ref.issue_count_n, ref.thumb_url_s or ""]


def _issue_ref(ref):
   return [int(ref.issue_key), ref.issue_num_s, ref.title_s, ref.thumb_url_s or ""]


def _run(action):
   try:
      return [action(), None, list(_requested)]
   except Exception, ex:
      return [None, ex.__class__.__name__ + ": " + str(ex), list(_requested)]


def search(serve, terms, ignored, max_results, publishers, before, after, threshold):
   _install(serve, max_results)
   calls = []
   def callback(done, total):
      calls.append([done, total])
      return False
   def action():
      refs = db.query_series_refs(terms, ignored, callback)
      refs = dbutils.filter_series_refs(refs, publishers, before, after, threshold)
      return [[_series(r) for r in refs], calls]
   return _run(action)


def issues(serve, series_id):
   _install(serve, 100)
   calls = []
   def callback(fraction):
      calls.append(fraction)
      return False
   def action():
      ref = SeriesRef(series_id, "oracle", -1, "", 0, None)
      return [[_issue_ref(r) for r in db.query_issue_refs(ref, callback)], calls]
   return _run(action)


def find_issue(serve, series_id, number):
   _install(serve, 100)
   def action():
      ref = db.query_issue_ref(SeriesRef(series_id, "oracle", -1, "", 0, None), number)
      return _issue_ref(ref) if ref else None
   return _run(action)


def _issue(i):
   return [int(i.issue_key), i.series_key, i.series_name_s, i.volume_year_n, i.issue_num_s, i.title_s,
      i.summary_s, i.publisher_s, i.imprint_s, i.pub_year_n, i.pub_month_n, i.pub_day_n,
      i.rel_year_n, i.rel_month_n, i.rel_day_n, i.crossovers_sl, i.characters_sl, i.teams_sl,
      i.locations_sl, i.writers_sl, i.pencillers_sl, i.inkers_sl, i.colorists_sl, i.letterers_sl,
      i.cover_artists_sl, i.editors_sl, i.webpage_s, i.rating_n, list(i.image_urls_sl)]


def issue_details(serve, issue_ids, slow_data):
   _install(serve, 100)
   def action():
      return [_issue(db.query_issue(IssueRef('1', key, '', None), slow_data)) for key in issue_ids]
   return _run(action)


def resolve(serve, text):
   _install(serve, 100)
   def action():
      ref = cvdb.__url_to_seriesref(text)
      return _series(ref) if ref else None
   return _run(action)


def check_magic_file(serve, path):
   _install(serve, 100)
   def action():
      ref = db.check_magic_file(path)
      return _series(ref) if ref else None
   return _run(action)


def summary(description):
   class Node(object):
      pass
   dom = Node()
   dom.results = Node()
   dom.results.description = description
   issue = Node()
   issue.summary_s = ''
   cvdb.__issue_parse_summary(issue, dom)
   return issue.summary_s


def cancel_then_repeat(serve, terms, series_id):
   ''' Cancels a series search and an issue load at their first progress report, then repeats both. '''
   _install(serve, 1000)
   ref = SeriesRef(series_id, "oracle", -1, "", 0, None)
   cancelled = [len(db.query_series_refs(terms, [], lambda done, total: True)),
      len(db.query_issue_refs(ref, lambda fraction: True))]
   repeated = [len(db.query_series_refs(terms, [], lambda done, total: False)),
      len(db.query_issue_refs(ref, lambda fraction: False))]
   return [cancelled, repeated, list(_requested)]


def url_path_encode(text):
   return HttpUtility.UrlPathEncode(text)
