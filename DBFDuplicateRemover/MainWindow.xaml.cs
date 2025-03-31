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
        public List<string> DuplicatesKeys { get; set; } = new List<string>(); // Список ключей удалённых дубликатов
    }

    public partial class MainWindow : Window
    {
        // Поле, в котором будет храниться путь к выбранной папке
        private string selectedFolder;

        public MainWindow()
        {
            InitializeComponent();
        }

        // Обработчик для кнопки "Выбрать папку"
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

        // Обработчик для кнопки "Запуск обработки"
        private void BtnProcess_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем, выбрана ли папка
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
                // Получаем все файлы с расширением .dbf в выбранной папке (без поддиректорий)
                var dbfFiles = Directory.GetFiles(selectedFolder, "*.dbf");

                // Если в папке нет DBF-файлов, сообщаем об этом и выходии
                if (dbfFiles.Length == 0)
                {
                    System.Windows.MessageBox.Show("В выбранной папке нет файлов DBF.",
                                    "Информация",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                    return;
                }

                // Список, где будеи накапливать информацию о дубликатах по каждому файлу
                var allResults = new List<DuplicateRemovalResult>();

                // Обрабатываем каждый файл, удаляя из него дубликаты
                foreach (var dbfFilePath in dbfFiles)
                {
                    // Возвращаем результат (сколько дубликатов и какие ключи)
                    var result = RemoveDuplicates(dbfFilePath);
                    allResults.Add(result);
                }

                // Формируем сводку для отображения в сообщении
                var sbSummary = new StringBuilder();
                sbSummary.AppendLine("Результаты обработки:");

                foreach (var r in allResults)
                {
                    // Показываем в форме только название файла и кол-во дубликатов
                    var fileNameOnly = Path.GetFileName(r.FileName);
                    sbSummary.AppendLine($"{fileNameOnly}: удалено дубликатов - {r.DuplicatesCount}");
                }

                // Показываем сводку в MessageBox
                System.Windows.MessageBox.Show(sbSummary.ToString(),
                                "Сводка",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);

                // Лог-файл в папке со всеми DBF, с датой и временем в названии
                string logFileName = Path.Combine(
                    selectedFolder,
                    $"dbfRemoveLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                // Записываем подробные сведения о дубликатах (только ключи)
                using (var writer = new StreamWriter(logFileName, false, Encoding.UTF8))
                {
                    writer.WriteLine("Лог-файл удаления дубликатов");
                    writer.WriteLine($"Создан: {DateTime.Now}");
                    writer.WriteLine("============================================");
                    writer.WriteLine();

                    foreach (var r in allResults)
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
                        else
                        {
                            writer.WriteLine("Дубликаты не найдены.");
                        }

                        writer.WriteLine("--------------------------------------------");
                        writer.WriteLine();
                    }
                }

                // Сообщаем пользователю, что всё готово
                System.Windows.MessageBox.Show($"Обработка всех файлов DBF завершена успешно!\n" +
                                $"Создан лог-файл: {Path.GetFileName(logFileName)}",
                                "Готово",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // В случае ошибки выводим сообщение
                System.Windows.MessageBox.Show("Ошибка обработки: " + ex.Message,
                                "Ошибка",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        // Метод, удаляющий дубликаты в одном DBF-файле и возвращающий информацию о дубликатах
        private DuplicateRemovalResult RemoveDuplicates(string filePath)
        {
            // Разрешаем использование дополнительных кодовых страниц
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Списки и структуры для хранения уникальных записей
            List<object[]> uniqueRecords = new List<object[]>();
            HashSet<string> keys = new HashSet<string>();

            // Список ключей, которые попали в «дубликаты» (для лога)
            List<string> removedDuplicates = new List<string>();

            DBFField[] originalFields;

            // Сначала считываем исходные записи из DBF
            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                using (DBFReader reader = new DBFReader(fileStream))
                {
                    // Устанавливаем кодировку для чтения DBF (CP866)
                    reader.CharEncoding = Encoding.GetEncoding(866);

                    // Запоминаем структуру полей (чтобы потом её же сохранить в выходном файле)
                    originalFields = reader.Fields;

                    // Находим индексы полей, по которым нужно сформировать ключ
                    int idxAddr = Array.FindIndex(originalFields,
                        f => f.Name.Equals("ADDRESID", StringComparison.OrdinalIgnoreCase));
                    int idxKylic = Array.FindIndex(originalFields,
                        f => f.Name.Equals("KYLIC", StringComparison.OrdinalIgnoreCase));
                    int idxNdom = Array.FindIndex(originalFields,
                        f => f.Name.Equals("NDOM", StringComparison.OrdinalIgnoreCase));
                    int idxNkorp = Array.FindIndex(originalFields,
                        f => f.Name.Equals("NKORP", StringComparison.OrdinalIgnoreCase));
                    int idxNkw = Array.FindIndex(originalFields,
                        f => f.Name.Equals("NKW", StringComparison.OrdinalIgnoreCase));
                    int idxNkomn = Array.FindIndex(originalFields,
                        f => f.Name.Equals("NKOMN", StringComparison.OrdinalIgnoreCase));
                    int idxMonthdbt = Array.FindIndex(originalFields,
                        f => f.Name.Equals("MONTHDBT", StringComparison.OrdinalIgnoreCase));

                    // Считываем каждую запись, формируем ключ из нужных полей, проверяем на уникальность
                    object[] record;
                    while ((record = reader.NextRecord()) != null)
                    {
                        // Формируем ключ на основе выбранных полей
                        string key = string.Join("|", new object[]
                        {
                            idxAddr  >= 0 ? record[idxAddr]  : "",
                            idxKylic >= 0 ? record[idxKylic] : "",
                            idxNdom  >= 0 ? record[idxNdom]  : "",
                            idxNkorp >= 0 ? record[idxNkorp] : "",
                            idxNkw   >= 0 ? record[idxNkw]   : "",
                            idxNkomn >= 0 ? record[idxNkomn] : ""
                        });

                        // Если такой ключ уже встречался, записываем его как «дубликат»
                        if (keys.Contains(key))
                        {
                            removedDuplicates.Add(key);
                        }
                        else
                        {
                            // При необходимости очищаем MONTHDBT (зависит от флажка chkClearMonthdbt)
                            if (idxMonthdbt > 0)
                            {
                                record[idxMonthdbt] = null;
                            }

                            keys.Add(key);
                            uniqueRecords.Add(record);
                        }
                    }
                }
            }

            // После того как все уникальные записи собраны, перезаписываем файл
            using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                using (DBFWriter writer = new DBFWriter(fileStream))
                {
                    // Снова устанавливаем кодировку
                    writer.CharEncoding = Encoding.GetEncoding(866);

                    writer.LanguageDriver = 0x65;

                    // Восстанавливаем исходные поля DBF
                    writer.Fields = originalFields;

                    // Записываем все уникальные записи обратно в файл
                    foreach (var record in uniqueRecords)
                    {
                        writer.WriteRecord(record);
                    }
                }
            }

            // Формируем объект-результат: какой файл, сколько удалено и какие были ключи
            var result = new DuplicateRemovalResult
            {
                FileName = filePath,
                DuplicatesCount = removedDuplicates.Count,
                DuplicatesKeys = removedDuplicates
            };

            return result;
        }
    }
}
