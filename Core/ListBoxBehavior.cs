using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows;

namespace ZC_ALM_TOOLS.Core
{
    public static class ListBoxBehavior
    {
        public static readonly DependencyProperty ScrollOnNewItemProperty =
            DependencyProperty.RegisterAttached("ScrollOnNewItem", typeof(bool),
            typeof(ListBoxBehavior), new PropertyMetadata(false, OnScrollOnNewItemChanged));

        public static void SetScrollOnNewItem(DependencyObject obj, bool value) => obj.SetValue(ScrollOnNewItemProperty, value);
        public static bool GetScrollOnNewItem(DependencyObject obj) => (bool)obj.GetValue(ScrollOnNewItemProperty);

        private static void OnScrollOnNewItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ListBox listBox && (bool)e.NewValue)
            {
                ((INotifyCollectionChanged)listBox.Items).CollectionChanged += (s, args) =>
                {
                    if (args.Action == NotifyCollectionChangedAction.Add)
                    {
                        listBox.ScrollIntoView(args.NewItems[0]);
                    }
                };
            }
        }
    }
}