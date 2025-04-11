using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        public bool IsEmpty { get; set; }  // признак того, что в полученный файл не заполнен суммами
        public bool IsSkipped { get; set; }  // признак того, что в файле нарушена структура и он будет пропущен
        public int DataKeysCount { get; set; }        // Сколько неправильных DTFACT в файле
        public List<int> DataKeys { get; set; } = new List<int>();  // Номера строк с неправильной DTFACT
    }

    public partial class MainWindow : Window
    {
        private string selectedFolder;

        public string DateFact { get; private set; }

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

                //  Записываем в переменную дату 1-го дня предыдущего месяца для сравение DTFACT в формате 01.01.2001
                DateTime now = DateTime.Now;
                DateFact = new DateTime(now.Year, now.Month, 1).AddMonths(-1).ToString("dd.MM.yyyy");

                if (dbfFiles.Length == 0)
                {
                    System.Windows.MessageBox.Show("В выбранной папке нет файлов DBF.",
                                    "Информация",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                    return;
                }

                var allResults = new List<DuplicateRemovalResult>();

                // Если проверяем заполненные файлы, то проверим имена файлов
                if (RadioB2.IsChecked == true && chTranslitFiles.IsChecked == true)
                {
                    // В цикле используем доп переменную index, чтобы дать различные имена файлам, которые только из русских букв
                    foreach (var (dbfFilePath, index) in dbfFiles.Select((dbfFilePath, index) => (dbfFilePath, index)))
                    {
                        //  Проверяем имя файла, допускаются только анг маленькие буквы a-z, 0-9 и спецсимволы "._" 
                        string prefix1 = Path.GetFileName(dbfFilePath);
                        //  Если есть лишние символы или строка начинается с цифр (но при этом если начинается с "!", то пропускаем)
                        if (prefix1[0] != '!' && !Regex.IsMatch(prefix1, @"^[a-z0-9._]+$") || Regex.IsMatch(prefix1, @"^\d+"))
                        {
                            // буквы делаем маленькие. убираем вначале имени файла цифры, если они есть и удаляем все лишние символы
                            prefix1 = Regex.Replace(prefix1, @"^\s*\d+\s*", string.Empty);    // Удаляем начальные пробелы и цифры
                            prefix1 = Translit(prefix1.ToLower()); // Заменяем все русские буквы на строчные латинские через метод Translit
                            prefix1 = Regex.Replace(prefix1, @"\s+", "_");                   // Заменяем оставшиеся пробелы на _
                            prefix1 = Regex.Replace(prefix1, @"[^a-z0-9._]", string.Empty); // Убираем лишние символы

                            if (prefix1 == ".dbf")       // Если осталось пустое имя файла, то даем свое
                            {
                                prefix1 = $"_unknow{index}.dbf";
                            }

                            prefix1 = Path.Combine(Path.GetDirectoryName(dbfFilePath), prefix1);  //  собираем полный путь файла
                            File.Move(dbfFilePath, prefix1);       // изменяем имя файла
                        }
                    }
                }

                dbfFiles = Directory.GetFiles(selectedFolder, "*.dbf");   // заново берем список файлов, вдруг были переименованы
                foreach (var dbfFilePath in dbfFiles)
                {
                    var result = RemoveDuplicates(dbfFilePath);

                    //  Если включена вторая радиокнопка и поставлена галочка на переименование файлов
                    if (RadioB2.IsChecked == true && chRenameFiles.IsChecked == true)
                    {
                        // Проверяем на ошибки и переименовываем файл в соотвествие с ошибками
                        if (result.FileName[0] != '!' && (result.IsSkipped || result.IsEmpty || result.DataKeysCount > 0))
                        {
                            string prefix0 = result.IsSkipped ? "!BAD_" : result.DataKeysCount > 0 ? "!DATE_" : "!EMPTY_";
                            result.FileName = prefix0 + result.FileName;
                            prefix0 = Path.Combine(Path.GetDirectoryName(dbfFilePath), result.FileName);
                            // Удаляем существующий файл, если он есть, и переименовываем старый файл
                            try
                            {
                                if (File.Exists(prefix0)) File.Delete(prefix0);
                                File.Move(dbfFilePath, prefix0);
                            }
                            catch (Exception ex) // Объединяем обработку исключений
                            {
                                System.Windows.MessageBox.Show($"Ошибка при переименовании файла:\n{ex.Message}",
                                                          "Ошибка",
                                                          MessageBoxButton.OK,
                                                          MessageBoxImage.Error
                                                                                );
                            }
                        }
                    }

                    allResults.Add(result);
                }

                // Подсчитываем общее кол-во проверенных файлов и сколько в них дублей (только для непропущенных)
                int totalFiles = dbfFiles.Length;
                int totalWithDuplicates = allResults
                    .Where(r => !r.IsSkipped)
                    .Count(r => r.DuplicatesCount > 0);

                int totalEmptyFiles = allResults
                   .Where(r => !r.IsSkipped)
                   .Count(r => r.IsEmpty);

                int totalDateBad = allResults
                   .Where(r => !r.IsSkipped)
                   .Count(r => r.DataKeysCount > 0);

                int totalBadFiles = allResults
                    .Count(r => r.IsSkipped);

                // Формируем имя лог-файла
                string logFileName = Path.Combine(
                    selectedFolder,
                    $"dbfRemoveLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                using (var writer = new StreamWriter(logFileName, false, Encoding.UTF8))
                {
                    writer.WriteLine("Лог-файл удаления дубликатов (с учётом поля SUMFIN)");
                    if (RadioB2.IsChecked == true)  // Если проверка входящих файлов, то добавляем доп записи
                    {
                        writer.WriteLine("Также с неверной датой DTFACT и нулевыми суммами");
                    }
                    writer.WriteLine($"Папка, в которой проверялись файлы: {selectedFolder}");
                    writer.WriteLine($"Создан: {DateTime.Now}");
                    writer.WriteLine("============================================");
                    writer.WriteLine();

                    if (RadioB2.IsChecked == true && totalEmptyFiles > 0)  // Если есть нулевые файлы, то предупреждаем о них
                    {
                        writer.WriteLine($"Внимание!!! Файлов с незаполненными суммами: {totalEmptyFiles}");
                        writer.WriteLine();
                    }

                    if (RadioB2.IsChecked == true && totalDateBad > 0)  // Если есть файлы с неверной DTFACT, то тоже предупреждаем
                    {
                        writer.WriteLine($"Внимание!!! Файлов с неверной датой DTFACT: {totalDateBad}");
                        writer.WriteLine();
                    }

                    if (RadioB2.IsChecked == true && (totalEmptyFiles > 0 || totalDateBad > 0))
                    {
                        writer.WriteLine("============================================");
                        writer.WriteLine();
                    }
                    writer.WriteLine();

                    if (totalDateBad > 0)
                    {
                        // Пишем о файлах, у которых неверная дата DTFACT
                        writer.WriteLine("Список файлов с неверной датой DTFACT:");
                        writer.WriteLine();
                        foreach (var r in allResults.Where(r => !r.IsSkipped && r.DataKeysCount > 0))
                        {
                            writer.WriteLine($"{r.FileName} в файле неверная DTFACT встречается {r.DataKeysCount} раз(а)");
                            if (r.DataKeysCount < 18)
                            {
                                writer.WriteLine($"в строках: {string.Join(", ", r.DataKeys)}");
                            }
                            else
                            {
                                writer.WriteLine("во многих строках, см. файл");
                            }
                            writer.WriteLine();
                        }
                        writer.WriteLine("--------------------------------------------");
                        writer.WriteLine();
                    }

                    if (totalEmptyFiles > 0)
                    {
                        // Пишем о файлах, у которых незаполнены суммы
                        writer.WriteLine("Список файлов с незаполненными суммами:");
                        writer.WriteLine();
                        foreach (var r in allResults.Where(r => !r.IsSkipped && r.IsEmpty))
                        {
                            writer.WriteLine($"{r.FileName} файл незаполненный, не содержит никаких сумм");
                            writer.WriteLine();
                        }
                        writer.WriteLine("--------------------------------------------");
                        writer.WriteLine();
                    }

                    if (totalBadFiles > 0)
                    {
                        // Пишем о файлах, у которых неверная структура
                        writer.WriteLine("Список файлов с нарушенной структурой:");
                        writer.WriteLine();
                        foreach (var r in allResults.Where(r => r.IsSkipped))
                        {
                            writer.WriteLine($"{r.FileName} у файла нарушена структура");
                            writer.WriteLine();
                        }
                        writer.WriteLine("--------------------------------------------");
                        writer.WriteLine();
                    }
                    // Пишем о файлах, у которых были дубликаты
                    foreach (var r in allResults.Where(r => !r.IsSkipped && r.DuplicatesCount > 0))
                    {
                        writer.WriteLine($"{r.FileName} в файле удалено дубликатов: {r.DuplicatesCount}");

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
                    writer.WriteLine($"Файлов с нарушенной структурой: {totalBadFiles}");
                }

                // Итоговое окно 
                var sbSummary = new StringBuilder();
                sbSummary.AppendLine($"Проверено файлов: {totalFiles}");
                sbSummary.AppendLine($"Из них с дублями: {totalWithDuplicates}");

                if (totalBadFiles > 0)
                {
                    sbSummary.AppendLine($"Файлов с нарушенной структурой: {totalBadFiles}");
                }
                if (RadioB2.IsChecked == true && totalEmptyFiles > 0)
                {
                    sbSummary.AppendLine($"Незаполненных файлов: {totalEmptyFiles}");
                }
                if (RadioB2.IsChecked == true && totalDateBad > 0)
                {
                    sbSummary.AppendLine($"Файлов с неверной датой DTFACT: {totalDateBad}");
                }

                if (totalDateBad > 0)
                {
                    sbSummary.AppendLine();
                    sbSummary.AppendLine();
                    sbSummary.AppendLine("Файлы с неверной датой DTFACT:");
                    if (totalDateBad < 13)
                    {
                        foreach (var r in allResults.Where(r => !r.IsSkipped && r.DataKeysCount > 0))
                        {
                            sbSummary.AppendLine($"{r.FileName}");
                        }
                    }
                    else { sbSummary.AppendLine("    их много, см. отчет"); }
                }

                if (totalEmptyFiles > 0)
                {
                    sbSummary.AppendLine();
                    sbSummary.AppendLine();
                    sbSummary.AppendLine("Файлы с незаполненными суммами:");
                    if (totalEmptyFiles < 13)
                    {
                        foreach (var r in allResults.Where(r => !r.IsSkipped && r.IsEmpty))
                        {
                            sbSummary.AppendLine($"{r.FileName}");
                        }
                    }
                    else { sbSummary.AppendLine("    их много, см. отчет"); }
                }
                sbSummary.AppendLine();
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


        /// Метод удаляющий дубликаты по ключу (ADDRESID, KYLIC, NDOM, NKORP, NKW, NKOMN),
        /// с учётом поля SUMFIN — среди дублей оставляем запись с наибольшим SUMFIN,
        /// при равенстве SUMFIN оставляем первую.

        private DuplicateRemovalResult RemoveDuplicates(string filePath)
        {
            
            var result = new DuplicateRemovalResult
            {
                FileName = Path.GetFileName(filePath),
                DuplicatesCount = 0,
                DataKeysCount = 0,
                IsEmpty = false,
                IsSkipped = false
            };

            // В этом списке мы временно храним записи (будем их заменять при необходимости)
            List<object[]> uniqueRecords = new List<object[]>();

            decimal summa1 = 0;  // В этой переменной будут складываться суммы в файле
                                 // Заносим в массив номера полей, из которых надо сложить числа (столбцы 16,20,28, и т.д.)
            int[] indexsum = new[] { 16, 20, 28, 30, 34, 36, 44 };

            // Словарь, хронящий данные какой индекс занимает запись, и каков SUMFIN у неё
            Dictionary<string, (decimal SumFin, int Index)> keyInfo
                = new Dictionary<string, (decimal SumFin, int Index)>();

            List<string> removedDuplicates = new List<string>();
            List<int> DataKeys = new List<int>();

            DBFField[] originalFields;

            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                using (DBFReader reader = new DBFReader(fileStream))
                {
                    reader.CharEncoding = Encoding.GetEncoding(866);
                    originalFields = reader.Fields;

                    // Проверяем наличие ключевых полей
                    int idxAddr = Array.FindIndex(originalFields, f => f.Name.Equals("ADDRESID", StringComparison.OrdinalIgnoreCase));
                    int idxKylic = Array.FindIndex(originalFields, f => f.Name.Equals("KYLIC", StringComparison.OrdinalIgnoreCase));
                    int idxNdom = Array.FindIndex(originalFields, f => f.Name.Equals("NDOM", StringComparison.OrdinalIgnoreCase));
                    int idxNkorp = Array.FindIndex(originalFields, f => f.Name.Equals("NKORP", StringComparison.OrdinalIgnoreCase));
                    int idxNkw = Array.FindIndex(originalFields, f => f.Name.Equals("NKW", StringComparison.OrdinalIgnoreCase));
                    int idxNkomn = Array.FindIndex(originalFields, f => f.Name.Equals("NKOMN", StringComparison.OrdinalIgnoreCase));
                    int idxSumfin = Array.FindIndex(originalFields, f => f.Name.Equals("SUMFIN", StringComparison.OrdinalIgnoreCase));

                    // Если не нашли хотя бы одно из основных полей — пропускаем файл
                    if (idxAddr < 0 || idxKylic < 0 || idxNdom < 0 ||
                        idxNkorp < 0 || idxNkw < 0 || idxNkomn < 0 || originalFields.Length != 54)
                    {
                        result.IsSkipped = true;
                        return result;
                    }

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

                        // Если включена галочка очищать MONTHDBT, что ставим значение null во все записи(строки) данного поля(столбца)
                        if (chkClearMonthdbt.IsChecked == true) { record[18] = null; }

                        // Определяем значение SUMFIN
                        decimal currentSumfin = 0;
                        if (idxSumfin >= 0 && record[idxSumfin] != null)
                        {
                            decimal.TryParse(record[idxSumfin].ToString(), out currentSumfin);
                        }

                        // Проверяем, встречался ли уже этот ключ
                        if (!keyInfo.ContainsKey(key))
                        {
                            // Впервые встречаем ключ => добавляем запись
                            int newIndex = uniqueRecords.Count;
                            uniqueRecords.Add(record);

                            keyInfo[key] = (currentSumfin, newIndex);

                        }
                        else
                        {
                            // Ключ уже есть => сравниваем SUMFIN
                            var (oldSumfin, oldIndex) = keyInfo[key];

                            if (currentSumfin > oldSumfin)
                            {
                                // Новый SUMFIN больше => заменяем старую запись на новую
                                removedDuplicates.Add(key);  // старая запись фактически «заменяется»

                                uniqueRecords[oldIndex] = record;
                                keyInfo[key] = (currentSumfin, oldIndex);

                            }
                            else if (currentSumfin == oldSumfin)
                            {
                                // SUMFIN одинаковый => оставляем первую (старую), а новая — дубликат
                                removedDuplicates.Add(key);
                            }
                            else
                            {
                                // Новый SUMFIN меньше старого => новая запись - дубликат
                                removedDuplicates.Add(key);
                            }
                        }
                    }

                    // Если включена вторая радиокнопка, то в цикле проверяем DTFACT каждой записи (строки) и складываем суммы из нужных полей
                    if (RadioB2.IsChecked == true)
                    {
                        DateTime dt; // Временная переменная для записи даты
                        for (int i = 0; i < uniqueRecords.Count; i++)
                        {
                            // Суммируем значения из полей массива, определенных в переменной indexsum, используя LINQ
                            summa1 += indexsum.Sum(index => decimal.TryParse(uniqueRecords[i][index].ToString(), out decimal value) ? value : 0);

                            // преобразуем 15 поле в дату и записываем в переменную dt, если там не дата, то dt = 1 января 1900 г.
                            dt = DateTime.TryParse(uniqueRecords[i][15]?.ToString(),
                                out DateTime parsedDate) ? parsedDate : new DateTime(1900, 1, 1);

                            // Если найдена неверная DTFACT (15-е поле), то записываем это в переменную DataKeys
                            if (dt.ToString("dd.MM.yyyy") != DateFact)
                            {
                                result.DataKeys.Add(i + 1);
                            }
                        }
                        // Если сумма равна 0, то ставим признак незаполненного файла
                        if (summa1 == 0) { result.IsEmpty = true; }
                        result.DataKeysCount = result.DataKeys.Count;
                    }
                }
            }


            // Перезаписываем файл теми записями, что остались в uniqueRecords
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

        // Метод, заменяющий во входной строке русские буквы на их латинские аналоги
        public static string Translit(string input)
        {
            // Определим соответствия кириллических букв и латинских
            var translitMap = new Dictionary<char, string>
    {
        {'а', "a"}, {'б', "b"}, {'в', "v"}, {'г', "g"}, {'д', "d"},
        {'е', "e"}, {'ё', "e"}, {'ж', "zh"}, {'з', "z"}, {'и', "i"},
        {'й', "y"}, {'к', "k"}, {'л', "l"}, {'м', "m"}, {'н', "n"},
        {'о', "o"}, {'п', "p"}, {'р', "r"}, {'с', "s"}, {'т', "t"},
        {'у', "u"}, {'ф', "f"}, {'х', "kh"}, {'ц', "ts"}, {'ч', "ch"},
        {'ш', "sh"}, {'щ', "sch"}, {'ъ', ""}, {'ы', "y"}, {'ь', ""},
        {'э', "e"}, {'ю', "yu"}, {'я', "ya"}
    };

            StringBuilder result = new StringBuilder(input.Length);

            foreach (char c in input)
            {
                string translit;
                if (translitMap.TryGetValue(c, out translit))
                    result.Append(translit);
                else
                    result.Append(c);
            }

            return result.ToString();
        }


        private void RadioB2_Unchecked(object sender, RoutedEventArgs e)
        {
            chRenameFiles.IsEnabled = false;
            chTranslitFiles.IsEnabled = false;
        }

        private void RadioB2_Checked(object sender, RoutedEventArgs e)
        {
            chRenameFiles.IsEnabled = true;
            chTranslitFiles.IsEnabled = true;
        }
    }
}
