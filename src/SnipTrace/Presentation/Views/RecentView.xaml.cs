using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using SnipTrace.App.Presentation.ViewModels;

namespace SnipTrace.App.Presentation.Views;

public partial class RecentView : UserControl
{
    public RecentView()
    {
        InitializeComponent();
    }

    public event Action<RecentItemView, RecentAction>? ActionRequested;

    public void SetItems(IReadOnlyList<RecentItemView> items)
    {
        List.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Open_Click(object sender, RoutedEventArgs e) => Raise(sender, RecentAction.Open);

    private void Locate_Click(object sender, RoutedEventArgs e) => Raise(sender, RecentAction.Locate);

    private void Delete_Click(object sender, RoutedEventArgs e) => Raise(sender, RecentAction.Delete);

    private void Raise(object sender, RecentAction action)
    {
        if (sender is FrameworkElement { DataContext: RecentItemView item }) ActionRequested?.Invoke(item, action);
    }
}

public enum RecentAction
{
    Open,
    Locate,
    Delete
}
