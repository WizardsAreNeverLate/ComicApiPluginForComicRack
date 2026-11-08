using System;
using System.Collections.Generic;

namespace ComicVineScraper.ComicVine;

/// <summary>Comic Vine imprint names and their parent publishers (cvimprints.py). Names match Comic Vine's spelling exactly.</summary>
internal static class Imprints
{
	private const string ActionLab = "Action Lab";
	private const string Amryl = "Amryl Entertainment";
	private const string Ape = "Ape Entertainment";
	private const string Avatar = "Avatar Press";
	private const string Boom = "Boom!";
	private const string DarkHorse = "Dark Horse Comics";
	private const string DC = "DC Comics";
	private const string Dynamite = "Dynamite Entertainment";
	private const string Hakusensha = "Hakusensha";
	private const string Heroic = "Heroic Publishing";
	private const string IDW = "IDW Publishing";
	private const string Image = "Image";
	private const string Kodansha = "Kodansha";
	private const string Lion = "Lion Forge Comics";
	private const string Malibu = "Malibu";
	private const string Marvel = "Marvel";
	private const string NBM = "Nbm";
	private const string Penguin = "Penguin Group";
	private const string Radio = "Radio Comix";
	private const string SLG = "Slg Publishing";
	private const string Titan = "Titan Comics";
	private const string Tokuma = "Tokuma Shoten";
	private const string Tokyopop = "Tokyopop";
	private const string Wizard = "Wizard";

	// Python's keys "Amerotica " and "Eurotica " had a trailing space and never matched (deviations #3).
	private static readonly Dictionary<string, string> Parents = new(StringComparer.Ordinal)
	{
		["2000AD"] = DC,
		["Adventure"] = Malibu,
		["Aircel Publishing"] = Malibu,
		["America's Best Comics"] = DC,
		["Amerotica"] = NBM,
		["Antimatter"] = Amryl,
		["Apparat"] = Avatar,
		["Archaia"] = Boom,
		["Berger Books"] = DarkHorse,
		["BOOM! Box"] = Boom,
		["Boundless Comics"] = Avatar,
		["Black Bull"] = Wizard,
		["Black Crown"] = IDW,
		["Blu Manga"] = Tokyopop,
		["CMX"] = DC,
		["Chaos! Comics"] = Dynamite,
		["Cliffhanger"] = DC,
		["Comic Bom Bom"] = Kodansha,
		["ComicsLit"] = NBM,
		["Curtis Magazines"] = Marvel,
		["Danger Zone"] = ActionLab,
		["Dark Horse Books"] = DarkHorse,
		["Dark Horse Manga"] = DarkHorse,
		["Desperado Publishing"] = Image,
		["Epic"] = Marvel,
		["Eternity"] = Malibu,
		["Eurotica"] = NBM,
		["Focus"] = DC,
		["Helix"] = DC,
		["Hero Comics"] = Heroic,
		["Homage comics"] = DC,
		["Hudson Street Press"] = Penguin,
		["Icon Comics"] = Marvel,
		["Impact"] = DC,
		["Jets Comics"] = Hakusensha,
		["KaBOOM!"] = Boom,
		["KiZoic"] = Ape,
		["Kodansha Comics Digital-First!"] = Kodansha,
		["Kodansha Comics USA"] = Kodansha,
		["MAD"] = DC,
		["Marvel Digital Comics Unlimited"] = Marvel,
		["Marvel Knights"] = Marvel,
		["Marvel Music"] = Marvel,
		["Marvel Soleil"] = Marvel,
		["Marvel UK"] = Marvel,
		["Maverick"] = DarkHorse,
		["Max"] = Marvel,
		["Milestone"] = DC,
		["Minx"] = DC,
		["Papercutz"] = NBM,
		["Paradox Press"] = DC,
		["Piranha Press"] = DC,
		["Quillion"] = Lion,
		["Razorline"] = Marvel,
		["Roar Comics"] = Lion,
		["ShadowLine"] = Image,
		["Silverline"] = Image,
		["Sin Factory Comix"] = Radio,
		["Skybound"] = Image,
		["Slave Labor"] = SLG,
		["Star Comics"] = Marvel,
		["Tangent Comics"] = DC,
		["Titan Books"] = Titan,
		["Todd McFarlane Productions"] = Image,
		["Tokuma Comics"] = Tokuma,
		["Top Cow"] = Image,
		["Top Shelf"] = IDW,
		["Ultraverse"] = Malibu,
		["Vertical"] = Kodansha,
		["Vertigo"] = DC,
		["Wildstorm"] = DC,
		["Zuda Comics"] = DC,
	};

	internal static IEnumerable<string> Names => Parents.Keys;

	/// <summary>The parent of a known imprint (matched after trimming), or the publisher unchanged.</summary>
	public static string FindParentPublisher(string publisher) =>
		Parents.TryGetValue(publisher.Trim(), out string? parent) ? parent : publisher;
}
