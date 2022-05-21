using System.Windows.Controls;

namespace ADB_Explorer.Converters
{
    public class CellConverter
    {
        public static DataGridCell GetDataGridCell(DataGridCellInfo cellInfo)
        {
            var cellContent = cellInfo.Column?.GetCellContent(cellInfo.Item);
            if (cellContent != null)
                return (DataGridCell)cellContent.Parent;

            return null;
        }
    }
}
