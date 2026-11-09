namespace ComicApiPlugin.Config;

/// <summary>Python's autochooseSeries and confirmIssue bools, which are mutually exclusive. One enum, so both-true cannot exist.</summary>
public enum ChooseMode
{
	Ask,
	AutoChooseSeries,
	ConfirmIssue,
}
