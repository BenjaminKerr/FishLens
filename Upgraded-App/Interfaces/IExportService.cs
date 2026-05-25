namespace FishLens_App.Interfaces
{
    public interface IExportService
    {
        void MakeExcelSheetAndInsertData(string excelPath, string csvPath);
    }
}
