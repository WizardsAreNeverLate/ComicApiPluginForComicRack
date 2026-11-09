using System.Drawing;
using System.Windows.Forms;

namespace ComicVineScraper.Plugin;

/// <summary>buttondgv.ButtonDataGridView, set up as the series and issue forms use it: Enter clicks OK instead of moving down a row.</summary>
internal sealed class ButtonGrid : DataGridView
{
	private readonly Button enter;

	public ButtonGrid(string name, Button enter, Point location, Size size)
	{
		this.enter = enter;
		Name = name;
		Location = location;
		Size = size;
		AllowUserToOrderColumns = true;
		SelectionMode = DataGridViewSelectionMode.FullRowSelect;
		MultiSelect = false;
		ReadOnly = true;
		RowHeadersVisible = false;
		AllowUserToAddRows = false;
		AllowUserToResizeRows = false;
		AllowUserToResizeColumns = false;
		DefaultCellStyle.NullValue = "--";
	}

	/// <summary>The index into the form's own list of the one selected row, read from the hidden model column so a sorted grid still maps back.</summary>
	public int? SelectedModel => SelectedRows.Count == 1 ? (int)SelectedRows[0].Cells["model"].Value : null;

	public DataGridViewColumn AddColumn(string name, string header, DataGridViewContentAlignment alignment, DataGridViewAutoSizeColumnMode size, bool visible = true)
	{
		var column = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Visible = visible, AutoSizeMode = size };
		column.DefaultCellStyle.Alignment = alignment;
		Columns.Add(column);
		return column;
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if (keyData == Keys.Enter)
		{
			enter.PerformClick();
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}
}
