using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using DotNetDBF;

namespace DBFDuplicateRemover
{
    public partial class MainWindow : Window
    {
        private string dbfFilePath;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "DBF Files (*.dbf)|*.dbf",
                Title = "Выберите DBF файл"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                dbfFilePath = openFileDialog.FileName;
                txtFilePath.Text = dbfFilePath;
            }
        }

        private void BtnProcess_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(dbfFilePath))
            {
                MessageBox.Show("Выберите файл перед обработкой.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                RemoveDuplicates(dbfFilePath);
                MessageBox.Show("Обработка завершена успешно.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка обработки: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveDuplicates(string filePath)
        {
            List<object[]> records = new List<object[]>();
            HashSet<string> uniqueKeys = new HashSet<string>();

            try
            {
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
                using (DBFReader reader = new DBFReader(fileStream))
                {
                    reader.CharEncoding = System.Text.Encoding.UTF8;

                    object[] record;
                    while ((record = reader.NextRecord()) != null)
                    {
                        string key = string.Join("|", record[0], record[1], record[2], record[3], record[4], record[5]);

                        if (!uniqueKeys.Contains(key))
                        {
                            record[6] = null; // Устанавливаем MONTHDBT в NULL
                            uniqueKeys.Add(key);
                            records.Add(record);
                        }
                    }
                }

                using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                using (DBFWriter writer = new DBFWriter(fileStream))
                {
                    writer.CharEncoding = System.Text.Encoding.UTF8;
                    writer.Fields = new DBFField[]
                    {
                new DBFField("ADDRESID", NativeDbType.Char, 20),
                new DBFField("KYLIC", NativeDbType.Char, 50),
                new DBFField("NDOM", NativeDbType.Char, 10),
                new DBFField("NKORP", NativeDbType.Char, 10),
                new DBFField("NKW", NativeDbType.Char, 10),
                new DBFField("NKOMN", NativeDbType.Char, 10),
                new DBFField("MONTHDBT", NativeDbType.Char, 10)
                    };

                    foreach (var record in records)
                    {
                        writer.AddRecord(record);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обработки файла: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
