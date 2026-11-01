using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using RamManager.ViewModels;

namespace RamManager.Views;

public partial class ProcessesView : UserControl
{
    private static readonly HashSet<string> TextColumns = ["Name", "Description"];

    public ProcessesView() => InitializeComponent();

    /// <summary>Numeric columns sort largest-first on the first click, like Task Manager.</summary>
    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        if (e.Column.SortDirection is not null || TextColumns.Contains(e.Column.SortMemberPath)) return;

        e.Handled = true;
        foreach (var column in ProcessGrid.Columns)
        {
            if (column != e.Column) column.SortDirection = null;
        }
        e.Column.SortDirection = ListSortDirection.Descending;

        var view = ProcessGrid.Items;
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(e.Column.SortMemberPath, ListSortDirection.Descending));
        }
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ProcessesViewModel vm && vm.OpenLocationCommand.CanExecute(null))
            vm.OpenLocationCommand.Execute(null);
    }
}
