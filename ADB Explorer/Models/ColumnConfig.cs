using System.Collections.Generic;

namespace ADB_Explorer.Models
{
    public enum ColumnType
    {
        Current = 1,
        Pending = 2,
        Completed = 4
    }

    public class FileOpColumn
    {
        private FileOpColumn(string name)
        {
            Name = name;
        }

        public FileOpColumn(string name, bool current = true, bool pending = true, bool completed = true) : this(name)
        {
            Type = current ? ColumnType.Current : 0;
            Type |= pending ? ColumnType.Pending : 0;
            Type |= completed ? ColumnType.Completed : 0;
        }

        public FileOpColumn(string name, ColumnType types) : this(name)
        {
            Type = types;
        }

        public string Name { get; set; }
        public ColumnType Type { get; set; }

        public bool IsType(ColumnType type)
        {
            return (Type & type) != 0;
        }
    }

    public class ColumnConfig
    {
        public ColumnConfig()
        {
            Title = null;
        }

        public ColumnConfig(List<ColumnConfig> items = null, List<string> columnList = null, bool isExisting = true, string title = null, string selected = null)
        {
            Title = title;
            Selected = selected;
            Items = items;
            ColumnList = columnList;

            ColumnList.Insert(0, isExisting ? "[Remove]" : "[Add]");
        }

        public List<string> ColumnList { get; set; }
        public List<ColumnConfig> Items { get; set; }
        public string Title { get; set; }
        public string Selected { get; set; }
    }
}
