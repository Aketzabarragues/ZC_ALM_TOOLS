namespace ZC_ALM_TOOLS.Core
{
    public static class DataHelper
    {
        public static string GetVal(string[] row, int index) =>
            (index >= 0 && index < row.Length) ? row[index].Trim() : "";

        public static int ParseInt(string val)
        {
            int.TryParse(val, out int result);
            return result;
        }
    }
}