namespace ADB_Explorer.Helpers;

public class DataGridHelper
{
    public static DataGridCell GetDataGridCell(DataGridCellInfo cellInfo)
    {
        var cellContent = cellInfo.Column?.GetCellContent(cellInfo.Item);
        if (cellContent != null)
            return (DataGridCell)cellContent.Parent;

        return null;
    }
}
