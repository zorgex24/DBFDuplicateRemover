using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using DotNetDBF;

namespace DBFDuplicateRemover
{
    // Класс для хранения информации о результатах удаления дубликатов из одного DBF-файла
    public class DuplicateRemovalResult
    {
        public string FileName { get; set; }            // Полный путь к файлу
        public int DuplicatesCount { get; set; }        // Сколько дубликатов было удалено
        public List<string> DuplicatesKeys { get; set; } = new List<string>();

        // Внутренний флаг: был ли файл пропущен из-за отсутствия ключевых полей
        public bool IsSkipped { get; set; }
    }

    public partial class MainWindow : Window
    {
        private string selectedFolder;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var folderBrowser = new FolderBrowserDialog())
            {
                var result = folderBrowser.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    selectedFolder = folderBrowser.SelectedPath;
                    txtFolderPath.Text = selectedFolder;
                }
            }
        }

        private void BtnProcess_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(selectedFolder))
            {
                System.Windows.MessageBox.Show("Выберите папку перед обработкой.",
                                "Ошибка",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dbfFiles = Directory.GetFiles(selectedFolder, "*.dbf");

                if (dbfFiles.Length == 0)
                {
                    System.Windows.MessageBox.Show("В выбранной папке нет файлов DBF.",
                                    "Информация",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                    return;
                }

                var allResults = new List<DuplicateRemovalResult>();

                foreach (var dbfFilePath in dbfFiles)
                {
                    var result = RemoveDuplicates(dbfFilePath);
                    allResults.Add(result);
                }

                // Подсчитываем общее кол-во проверенных файлов и сколько в них дублей (только для непропущенных)
                int totalFiles = dbfFiles.Length;
                int totalWithDuplicates = allResults
                    .Where(r => !r.IsSkipped)      // пропущенные не считаем
                    .Count(r => r.DuplicatesCount > 0);

                // Формируем имя лог-файла
                string logFileName = Path.Combine(
                    selectedFolder,
                    $"dbfRemoveLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                using (var writer = new StreamWriter(logFileName, false, Encoding.UTF8))
                {
                    writer.WriteLine("Лог-файл удаления дубликатов");
                    writer.WriteLine($"Создан: {DateTime.Now}");
                    writer.WriteLine("============================================");
                    writer.WriteLine();

                    // Пишем о файлах, у которых были дубликаты
                    foreach (var r in allResults.Where(r => !r.IsSkipped && r.DuplicatesCount > 0))
                    {
                        writer.WriteLine($"Файл: {r.FileName}");
                        writer.WriteLine($"Удалено дубликатов: {r.DuplicatesCount}");

                        if (r.DuplicatesKeys.Count > 0)
                        {
                            writer.WriteLine("Список ключей удалённых дубликатов:");
                            foreach (var key in r.DuplicatesKeys)
                            {
                                writer.WriteLine("  " + key);
                            }
                        }
                        writer.WriteLine("--------------------------------------------");
                        writer.WriteLine();
                    }

                    // Итоговые данные 
                    writer.WriteLine("--------------------------------------------");
                    writer.WriteLine($"Всего проверено файлов: {totalFiles}");
                    writer.WriteLine($"С дублями: {totalWithDuplicates}");
                }

                // Итоговое окно 
                var sbSummary = new StringBuilder();
                sbSummary.AppendLine($"Проверено файлов: {totalFiles}");
                sbSummary.AppendLine($"Из них с дублями: {totalWithDuplicates}");
                sbSummary.AppendLine();
                sbSummary.AppendLine($"Подробный отчет в файле: {Path.GetFileName(logFileName)}");

                System.Windows.MessageBox.Show(sbSummary.ToString(),
                                "Сводка",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Ошибка обработки: " + ex.Message,
                                "Ошибка",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        private DuplicateRemovalResult RemoveDuplicates(string filePath)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var result = new DuplicateRemovalResult
            {
                FileName = filePath,
                DuplicatesCount = 0,
                IsSkipped = false
            };

            List<object[]> uniqueRecords = new List<object[]>();
            HashSet<string> keys = new HashSet<string>();
            List<string> removedDuplicates = new List<string>();

            DBFField[] originalFields;

            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                using (DBFReader reader = new DBFReader(fileStream))
                {
                    reader.CharEncoding = Encoding.GetEncoding(866);
                    originalFields = reader.Fields;

                    // Проверяем наличие нужных полей
                    int idxAddr = Array.FindIndex(originalFields, f => f.Name.Equals("ADDRESID", StringComparison.OrdinalIgnoreCase));
                    int idxKylic = Array.FindIndex(originalFields, f => f.Name.Equals("KYLIC", StringComparison.OrdinalIgnoreCase));
                    int idxNdom = Array.FindIndex(originalFields, f => f.Name.Equals("NDOM", StringComparison.OrdinalIgnoreCase));
                    int idxNkorp = Array.FindIndex(originalFields, f => f.Name.Equals("NKORP", StringComparison.OrdinalIgnoreCase));
                    int idxNkw = Array.FindIndex(originalFields, f => f.Name.Equals("NKW", StringComparison.OrdinalIgnoreCase));
                    int idxNkomn = Array.FindIndex(originalFields, f => f.Name.Equals("NKOMN", StringComparison.OrdinalIgnoreCase));
                    int idxMonthdbt = Array.FindIndex(originalFields, f => f.Name.Equals("MONTHDBT", StringComparison.OrdinalIgnoreCase));

                    // Если не нашли хотя бы одно из основных полей — пропускаем файл
                    if (idxAddr < 0 || idxKylic < 0 || idxNdom < 0 ||
                        idxNkorp < 0 || idxNkw < 0 || idxNkomn < 0)
                    {
                        result.IsSkipped = true;
                        return result;
                    }

                    // Иначе собираем уникальные записи
                    object[] record;
                    while ((record = reader.NextRecord()) != null)
                    {
                        // Формируем ключ
                        string key = string.Join("|", new object[]
                        {
                            record[idxAddr],
                            record[idxKylic],
                            record[idxNdom],
                            record[idxNkorp],
                            record[idxNkw],
                            record[idxNkomn]
                        });

                        // Проверяем на дубликат
                        if (keys.Contains(key))
                        {
                            removedDuplicates.Add(key);
                        }
                        else
                        {
                            // Здесь учитывается чекбокс очистки MONTHDBT
                            if (chkClearMonthdbt.IsChecked == true && idxMonthdbt >= 0)
                            {
                                 record[idxMonthdbt] = null;
                            }

                            keys.Add(key);
                            uniqueRecords.Add(record);
                        }
                    }
                }
            }

            // Если не пропущен, делаем перезапись файла
            using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                using (DBFWriter writer = new DBFWriter(fileStream))
                {
                    writer.CharEncoding = Encoding.GetEncoding(866);
                    writer.LanguageDriver = 0x65; 

                    writer.Fields = originalFields;

                    foreach (var rec in uniqueRecords)
                    {
                        writer.WriteRecord(rec);
                    }
                }
            }

            // Заполняем остаток результата
            result.DuplicatesCount = removedDuplicates.Count;
            result.DuplicatesKeys = removedDuplicates;

            return result;
        }
    }
}
