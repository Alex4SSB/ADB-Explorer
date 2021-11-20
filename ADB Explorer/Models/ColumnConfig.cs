using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ADB_Explorer.Models
{
    public static class ColumnSettings
    {
        public static List<string> ColumnList { get; set; }

        public static ObservableCollection<ColumnConfig> Configs { get; set; } = new();
    }

    public class ColumnConfig
    {
        public ColumnConfig()
        {
            Title = null;
        }

        public ColumnConfig(List<ColumnConfig> items = null, string title = null, string selected = null)
        {
            Title = title;
            Selected = selected;
            Items = items;
        }

        public static List<string> ColumnList => ColumnSettings.ColumnList;
        public List<ColumnConfig> Items { get; set; }
        public string Title { get; set; }
        public string Selected { get; set; }
    }
}
