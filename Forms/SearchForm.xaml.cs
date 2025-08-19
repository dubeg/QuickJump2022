using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using EnvDTE;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using QuickJump2022.Interop;
using QuickJump2022.Models;
using QuickJump2022.QuickJump.Tools;
using QuickJump2022.Services;
using QuickJump2022.Text;
using QuickJump2022.Tools;
using static QuickJump2022.Models.Enums;
using Rect = System.Windows.Rect;

namespace QuickJump2022.Forms;

public partial class SearchForm : DialogWindow, INotifyPropertyChanged {
    private DismissOnClickOutsideBounds _dismissOnClickOutsideBounds;
    public int PageSize => 20; // TODO: make it configurable
    public static readonly DependencyProperty HintFontSizeProperty = DependencyProperty.Register(
        nameof(HintFontSize), typeof(double), typeof(SearchForm), new PropertyMetadata(0d)
    );
    public double HintFontSize { get => (double)GetValue(HintFontSizeProperty); set => SetValue(HintFontSizeProperty, value); }
    public SearchInstance SearchInstance { get; init; }
    public GoToService GoToService { get; init; }
    public CommandService CommandService { get; init; }
    public ClassificationService ClassificationService { get; init; }
    private bool _useSymbolColors = false;
    private DTE _dte;
    private List<ListItemViewModel> Items = new();
    public string searchText { get; set; } = "test";


    public static async Task ShowModalAsync(QuickJumpPackage package, ESearchType searchType) {
        var dialog = new SearchForm(package, searchType);
        await dialog.LoadDataAsync();
        dialog.ShowModal();
    }

    protected SearchForm(QuickJumpPackage package, ESearchType searchType) {
        InitializeComponent();  
        var searchInstance = new SearchInstance(
            package.ProjectFileService,
            package.SymbolService,
            package.CommandService,
            searchType,
            package.GeneralOptions.FileSortType,
            package.GeneralOptions.CSharpSortType,
            package.GeneralOptions.MixedSortType
        );
        SearchInstance = searchInstance;
        GoToService = package.GoToService;
        CommandService = package.CommandService;
        ClassificationService = package.ClassificationService;
        _dte = package.Dte;
        _useSymbolColors = package.GeneralOptions.UseSymbolColors;
        // --
        WindowStartupLocation = WindowStartupLocation.Manual;
        this.Loaded += (s, e) => AdjustPosition();
        this.SizeChanged += (s, e) => AdjustPosition();
        _dismissOnClickOutsideBounds = new(this);
        // --
        var (fontFamily, fontSize) = FontsAndColorsHelper.GetEditorFontInfo(true);
        FontFamily = fontFamily;
        FontSize = fontSize < 14 ? fontSize + 2 : fontSize; // TODO: configure via options (?)
        HintFontSize = Math.Max(8, fontSize - 1);
        Width = searchType == ESearchType.Files ? 800 : 600; // TODO: configure via options
        searchText = "test";
    }


    private void TextBuffer_Changed(object sender, TextContentChangedEventArgs e) {
        RefreshList();
        if (Items.Count > 0) {
            lstItems.SelectedIndex = 0;
            EnsureSelectedItemIsVisible();
        }
    }

    private void AdjustPosition() {
        ThreadHelper.ThrowIfNotOnUIThread();
        var vsWindowRect = new Rect();
        var vsWindow = _dte.MainWindow;
        vsWindowRect = vsWindow.WindowState == EnvDTE.vsWindowState.vsWindowStateMaximize
            ? WindowUtils.GetMaximizedWindowBounds(vsWindow.HWnd)
            : new Rect(vsWindow.Left, vsWindow.Top, vsWindow.Width, vsWindow.Height);
        var activeDocWindow = _dte.ActiveDocument?.ActiveWindow;
        if (activeDocWindow is not null) {
            vsWindowRect = new Rect(activeDocWindow.Left, activeDocWindow.Top, activeDocWindow.Width, activeDocWindow.Height);
        }
        vsWindowRect = Application.Current.MainWindow.DeviceToLogicalRect(vsWindowRect);
        this.Top = vsWindowRect.Top + 75; // TODO: make configurable in options
        this.Left = vsWindowRect.Left
            + (vsWindowRect.Width / 2.0)
            - (ActualWidth / 2.0);
    }

    public async Task LoadDataAsync() {
        await SearchInstance.LoadDataAsync();
        RefreshList();
        if (Items.Count > 0) {
            lstItems.SelectedIndex = 0;
            EnsureSelectedItemIsVisible();
        }
    }

    private void RefreshList() {
        var listItems = new List<ListItemViewModel>();
        var results = SearchInstance.Search(searchText);
        var brush = Application.Current.TryFindResource(ThemedDialogColors.ListBoxTextBrushKey) as System.Windows.Media.Brush;
        foreach (var item in results) {
            var viewModel = new ListItemViewModel(item);
            if (item is ListItemSymbol symbol && _useSymbolColors) {
                var fgBrush = ClassificationService.GetFgColorForClassification(symbol.Item.BindType);
                viewModel.NameForeground = fgBrush;
            }
            else {
                viewModel.NameForeground = brush;
            }
            listItems.Add(viewModel);
        }
        lstItems.ItemsSource = Items = listItems;
    }

    // Text changes are handled via the VS editor buffer change event

    private async void EditorHost_PreviewKeyDown(object sender, KeyEventArgs e) {
        // Note: We don't handle Left/Right arrows, Ctrl+Shift+Left/Right, etc.
        // so they work normally for text navigation and selection
        if (e.Key == Key.Escape) {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Return) {
            await GoToItem(true);
            e.Handled = true;
        }
        else if (e.Key == Key.Up) {
            if (lstItems.SelectedIndex > 0) {
                lstItems.SelectedIndex--;
                EnsureSelectedItemIsVisible();
            }
            await GoToItem();
            e.Handled = true;
        }
        else if (e.Key == Key.Down) {
            if (lstItems.SelectedIndex < Items.Count - 1) {
                lstItems.SelectedIndex++;
                EnsureSelectedItemIsVisible();
            }
            await GoToItem();
            e.Handled = true;
        }
        if (e.Key == Key.PageUp) {
            if (lstItems.SelectedIndex >= PageSize)
                lstItems.SelectedIndex -= PageSize;
            else
                lstItems.SelectedIndex = 0;
            EnsureSelectedItemIsVisible();
            await GoToItem();
            e.Handled = true;
        }
        else if (e.Key == Key.PageDown) {
            if (lstItems.SelectedIndex < Items.Count - PageSize)
                lstItems.SelectedIndex += PageSize;
            else
                lstItems.SelectedIndex = Items.Count - 1;
            EnsureSelectedItemIsVisible();
            await GoToItem();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Go to selected item.
    /// </summary>
    /// <param name="commit">
    /// False => Go to item without closing the dialog.
    /// True => Go to item and close the dialog.
    /// If the item is a command, it will be executed only on commit.
    /// If the item is a file or symbol, it will always be navigated to.
    /// </param>
    /// <returns></returns>
    private async Task GoToItem(bool commit = false) {
        var selectedItem = lstItems.SelectedItem as ListItemViewModel;
        if (selectedItem != null) {
            if (commit) Close();
            var listItem = selectedItem.Item;
            if (listItem is ListItemFile file) await GoToService.GoToFileAsync(file);
            else if (listItem is ListItemSymbol symbol) await GoToService.GoToSymbolAsync(symbol);
            else if (listItem is ListItemCommand command) {
                if (commit) {
                    // The dialog must be closed before executing a command
                    // in case the command opens another modal dialog.
                    CommandService.Execute(command.Item);
                    return;
                }
            }
            else if (listItem is ListItemKnownCommand knownCommand) {
                if (commit) {
                    // The dialog must be closed before executing a command bar button
                    // in case it opens another modal dialog.
                    CommandService.Execute(knownCommand.Item.Command);
                    return;
                }
            }
        }
    }

    private void EnsureSelectedItemIsVisible() {
        if (lstItems.SelectedItem != null) {
            lstItems.ScrollIntoView(lstItems.SelectedItem);
        }
    }

    private async void lstItems_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        var item = (e.OriginalSource as FrameworkElement)?.DataContext as ListItemViewModel;
        if (item != null) {
            await GoToItem(true);
        }
    }

    private void lstItems_PreviewKeyUp(object sender, KeyEventArgs e) {
        //_textView?.VisualElement.Focus();
    }
    
    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
