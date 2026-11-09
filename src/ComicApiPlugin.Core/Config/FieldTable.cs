using System.Collections.Generic;
using System.Linq;
using ComicApiPlugin.Books;

namespace ComicApiPlugin.Config;

/// <summary>
/// The one list of user-toggleable fields, in ConfigForm display order. ConfigForm's CheckedListBox, the settings.dat
/// update* keys, and the label keys all derive from this table.
/// </summary>
public static class FieldTable
{
	/// <param name="SettingsKey">The settings.dat key, for example "updateWriter".</param>
	/// <param name="LabelTextKey">The key in Script.ComicVineScraper.xml of the checkbox label.</param>
	/// <param name="Controls">The bits the setting switches. Published switches year, month, and day together.</param>
	public sealed record Row(string SettingsKey, string LabelTextKey, BookFields Controls);

	public static IReadOnlyList<Row> Toggleable { get; } =
	[
		new("updateSeries", "ConfigFormSeriesCB", BookFields.Series),
		new("updateVolume", "ConfigFormVolumeCB", BookFields.Volume),
		new("updateNumber", "ConfigFormNumberCB", BookFields.Number),
		new("updateTitle", "ConfigFormTitleCB", BookFields.Title),
		new("updatePublished", "ConfigFormPublishedCB", BookFields.Published),
		new("updateReleased", "ConfigFormReleasedCB", BookFields.Released),
		new("updateCrossovers", "ConfigFormCrossoversCB", BookFields.Crossovers),
		new("updatePublisher", "ConfigFormPublisherCB", BookFields.Publisher),
		new("updateImprint", "ConfigFormImprintCB", BookFields.Imprint),
		new("updateWriter", "ConfigFormWriterCB", BookFields.Writers),
		new("updatePenciller", "ConfigFormPencillerCB", BookFields.Pencillers),
		new("updateInker", "ConfigFormInkerCB", BookFields.Inkers),
		new("updateColorist", "ConfigFormColoristCB", BookFields.Colorists),
		new("updateLetterer", "ConfigFormLettererCB", BookFields.Letterers),
		new("updateCoverArtist", "ConfigFormCoverCB", BookFields.CoverArtists),
		new("updateEditor", "ConfigFormEditorCB", BookFields.Editors),
		new("updateSummary", "ConfigFormSummaryCB", BookFields.Summary),
		new("updateCharacters", "ConfigFormCharactersCB", BookFields.Characters),
		new("updateTeams", "ConfigFormTeamsCB", BookFields.Teams),
		new("updateLocations", "ConfigFormLocationsCB", BookFields.Locations),
		new("updateWebpage", "ConfigFormWebCB", BookFields.Webpage),
	];

	/// <summary>Every bit the 21 rows control.</summary>
	public static BookFields All { get; } = Toggleable.Aggregate(BookFields.None, (all, row) => all | row.Controls);
}
