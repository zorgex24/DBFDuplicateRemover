using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
                MessageBox.Show("Выберите файл перед обработкой.", "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                RemoveDuplicates(dbfFilePath);
                MessageBox.Show("Обработка завершена успешно.", "Готово",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка обработки: " + ex.Message, "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveDuplicates(string filePath)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            List<object[]> uniqueRecords = new List<object[]>();

            HashSet<string> keys = new HashSet<string>();

            DBFField[] originalFields;

            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                using (DBFReader reader = new DBFReader(fileStream))
                {
                    reader.CharEncoding = Encoding.GetEncoding(866);
                    originalFields = reader.Fields; 

                    
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

                    object[] record;
                    while ((record = reader.NextRecord()) != null)
                    {
                       
                        string key = string.Join("|", new object[]
                        {
                            idxAddr  >= 0 ? record[idxAddr]  : "",
                            idxKylic >= 0 ? record[idxKylic] : "",
                            idxNdom  >= 0 ? record[idxNdom]  : "",
                            idxNkorp >= 0 ? record[idxNkorp] : "",
                            idxNkw   >= 0 ? record[idxNkw]   : "",
                            idxNkomn >= 0 ? record[idxNkomn] : ""
                        });

                       
                        if (!keys.Contains(key))
                        {
                           
                            if (idxMonthdbt >= 0)
                                record[idxMonthdbt] = null;

                            keys.Add(key);
                            uniqueRecords.Add(record);
                        }
                       
                    }
                }
            }

            
            using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                using (DBFWriter writer = new DBFWriter(fileStream))
                {
                    writer.CharEncoding = Encoding.GetEncoding(866);
                  
                    writer.Fields = originalFields;

                  
                    foreach (var record in uniqueRecords)
                    {
                        writer.WriteRecord(record);
                    }
                }
            }
        }
    }
}
