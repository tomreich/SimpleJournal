/*
 * Copyright (c) 2017 Tom Reich
 * 
 * Licensed under the Microsoft Public License (MS-PL) (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *  https://msdn.microsoft.com/en-us/library/ff649456.aspx
 *  or
 *  https://opensource.org/licenses/MS-PL
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Ionic.Zip;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;
using System.Configuration;

namespace SimpleJournal
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private EntryFile _selectedFile;
        private byte[] _selectedFileBytes;
        private string _journalFilePath;
        private string _pw;
        private bool _calendarReload = true;

        public MainWindow()
        {
            InitializeComponent();
        }

        #region Load Methods
        private bool OpenJournalFile(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            string pass = TextPrompt.Prompt($"Enter Password for Journal {new FileInfo(filePath).Name}:", "Password?", string.Empty, TextPrompt.InputType.Password);
            if (string.IsNullOrWhiteSpace(pass))
                return false;

            _pw = pass;

            using (ZipFile file = ZipFile.Read(filePath))
            {
                // Put file names in list.
                LoadFileList(GetFileNames(file));
            }

            _journalFilePath = filePath;
            MenuItem_Save.IsEnabled = MenuItem_Delete.IsEnabled = MenuItem_NewEntry.IsEnabled = MenuItem_Revert.IsEnabled = MenuItem_SelectAllDates.IsEnabled = true;

            return true;
        }

        private void LoadFileList(List<string> allFiles, SelectedDatesCollection filter = null)
        {
            // Put file names in list.
            fileListBox.Items.Clear();
            allFiles.Sort();
            allFiles.Reverse();
            _calendarReload = false;
            entriesCalendar.SelectedDate = null;
            foreach(string fileName in allFiles)
            {
                EntryFile entryFile = new EntryFile() { FileName = fileName };
                if(entryFile.FileDateTime != DateTime.MinValue && (filter == null || filter.Contains(entryFile.FileDate)))
                {
                    fileListBox.Items.Add(entryFile);
                    entriesCalendar.SelectedDates.Add(entryFile.FileDateTime);
                }
            }
            _calendarReload = true;
        }

        private List<string> GetFileNames(ZipFile file)
        {
            List<string> ret = new List<string>();
            file.Entries.ToList().ForEach(x => ret.Add(x.FileName));
            return ret;
        }

        private bool LoadFileFromZip(ZipFile file, string fileName, string pw)
        {
            foreach (ZipEntry entry in file)
            {
                if (entry.FileName == fileName)
                {
                    TextRange range = new TextRange(entryTextBox.Document.ContentStart, entryTextBox.Document.ContentEnd);
                    using (MemoryStream ms = new MemoryStream())
                    {
                        try
                        {
                            entry.ExtractWithPassword(ms, pw);
                        }
                        catch (BadPasswordException)
                        {
                            MessageBox.Show("Unable to load entry.  Bad password?", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return false;
                        }
                        
                        ms.Position = 0;
                        range.Load(ms, DataFormats.Rtf);
                    }

                    using (MemoryStream ms = new MemoryStream())
                    { 
                        // Save bytes so we can do a comparison later to see if we need to save.
                        ms.Position = 0;
                        range.Save(ms, DataFormats.Rtf);
                        _selectedFileBytes = ms.ToArray();
                    }
                    return true;
                }
            }
            return false;
        }
        #endregion

        #region Delete Methods
        private void DeleteFileFromZip(ZipFile file, string fileName, string pw)
        {
            file.Password = pw;
            file.Encryption = EncryptionAlgorithm.WinZipAes256;
            file.RemoveEntry(fileName);
        }

        private void DeleteSelectedEntry(bool dontPrompt)
        {
            if (_selectedFile != null && !string.IsNullOrWhiteSpace(_journalFilePath))
            {
                if (dontPrompt || MessageBox.Show("Are you sure you want to delete the selected entry?  This cannot be undone!", "Save?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    // We have changes and the user wants to save them.
                    using (ZipFile file = ZipFile.Read(_journalFilePath))
                    {
                        DeleteFileFromZip(file, _selectedFile.FileName, _pw);
                        file.Save();
                        _selectedFileBytes = null;
                        _selectedFile = null;
                        LoadFileList(GetFileNames(file));
                    }
                    fileListBox.SelectedIndex = 0;
                    FocusManager.SetFocusedElement(entryGrid, entryTextBox);
                }
            }
        }
        #endregion

        #region Save Methods
        private void SaveFileToZip(ZipFile file, string fileName, Stream content, string pw)
        {
            file.Password = pw;
            file.Encryption = EncryptionAlgorithm.WinZipAes256;
            file.UpdateEntry(fileName, content);
        }

        private void SaveSelectedEntry(bool dontPrompt)
        {
            if (_selectedFile != null && _selectedFileBytes != null && !string.IsNullOrWhiteSpace(_journalFilePath))
            {
                TextRange range = new TextRange(entryTextBox.Document.ContentStart, entryTextBox.Document.ContentEnd);
                using (MemoryStream ms = new MemoryStream())
                {
                    range.Save(ms, DataFormats.Rtf);

                    // Compare file bytes to see if we have any changes
                    ms.Position = 0;
                    byte[] newBytes = ms.ToArray();

                    if ((newBytes.Length != _selectedFileBytes.Length || !Enumerable.SequenceEqual(newBytes, _selectedFileBytes)) &&
                        (dontPrompt || MessageBox.Show("Save your changes?", "Save?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes))
                    {
                        // We have changes and the user wants to save them.
                        ms.Position = 0;
                        using (ZipFile file = ZipFile.Read(_journalFilePath))
                        {
                            SaveFileToZip(file, _selectedFile.FileName, ms, _pw);
                            file.Save();
                        }
                        _selectedFileBytes = newBytes;
                    }
                }
            }
        }
        #endregion

        #region Calendar & File List Methods
        private void Calendar_SelectAll_Click(object sender, RoutedEventArgs e)
        {
            SaveSelectedEntry(false);
            _calendarReload = false;
            using (ZipFile file = ZipFile.Read(_journalFilePath))
            {
                // Put file names in list.
                LoadFileList(GetFileNames(file));
            }
        }

        private void Calendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_journalFilePath)) return;
            if (!_calendarReload) return;

            SaveSelectedEntry(false);
            using (ZipFile file = ZipFile.Read(_journalFilePath))
            {
                // Put file names in list.
                LoadFileList(GetFileNames(file), entriesCalendar.SelectedDates);
            }
        }

        private void fileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveSelectedEntry(false);

            if (fileListBox.SelectedValue != null)
            {
                _selectedFile = fileListBox.SelectedValue as EntryFile;

                using (ZipFile file = ZipFile.Read(_journalFilePath))
                {
                    if(LoadFileFromZip(file, _selectedFile.FileName, _pw))
                        TitleLabel.Content = _selectedFile.FileDateTime.ToString("dddd, MMMM d yyyy, hh:mm:ss tt");
                }
            }
        }
        #endregion

        #region Form Load & Unload
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string defaultFile = Properties.Settings.Default["DefaultFile"].ToString();
            BackupOnExit.IsChecked = bool.Parse(Properties.Settings.Default["BackupOnExit"].ToString());
            if (!string.IsNullOrWhiteSpace(defaultFile) && File.Exists(defaultFile))
            {
                OpenJournalFile(defaultFile);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveSelectedEntry(false);
            if(!string.IsNullOrWhiteSpace(_journalFilePath) && BackupOnExit.IsChecked)
            {
                FileInfo fi = new FileInfo(_journalFilePath);
                File.Copy(_journalFilePath, Path.Combine(fi.DirectoryName, fi.Name.Replace(fi.Extension, string.Empty) + "_" + DateTime.Now.ToString("s").Replace(':', '_') + fi.Extension));
            }
        }
        #endregion

        #region Menubar Click Actions
        private void Open_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog() { DefaultExt = ".zip", Filter = "Zip files (*.zip)|*.zip" };
            if (ofd.ShowDialog() == true)
            {
                _selectedFile = null;

                if (OpenJournalFile(ofd.FileName))
                {
                    Properties.Settings.Default["DefaultFile"] = _journalFilePath;
                    Properties.Settings.Default.Save();
                }
            }
        }


        private void NewEntry_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_journalFilePath)) return;

            SaveSelectedEntry(false);

            using (MemoryStream ms = new MemoryStream())
            {
                using (ZipFile file = ZipFile.Read(_journalFilePath))
                {
                    file.AddEntry(DateTime.Now.ToString("s").Replace(':', '_') + ".rtf", "{\\rtf1}");
                    file.Save();
                    LoadFileList(GetFileNames(file));
                }
                fileListBox.SelectedIndex = 0;
                FocusManager.SetFocusedElement(entryGrid, entryTextBox);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveSelectedEntry(true);
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog() { DefaultExt = ".zip", Filter = "Zip files (*.zip)|*.zip" };
            if (sfd.ShowDialog() == true)
            {
                _selectedFile = null;

                using (ZipFile file = new ZipFile(sfd.FileName))
                {
                    file.AddEntry(DateTime.Now.ToString("s").Replace(':', '_') + ".rtf", "{\\rtf1}");
                    file.Save();
                }

                //_journalFilePath = sfd.FileName;
                //NewEntry_Click(sender, e);
                OpenJournalFile(sfd.FileName);
            }
        }

        private void RevertEntry_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBoxResult.Yes == MessageBox.Show("Are you sure you want to revert to the saved version?  You will lose all changes since your last save!", "Revert?", MessageBoxButton.YesNo, MessageBoxImage.Asterisk))
            {
                TextRange range = new TextRange(entryTextBox.Document.ContentStart, entryTextBox.Document.ContentEnd);
                using (MemoryStream ms = new MemoryStream(_selectedFileBytes))
                {
                    range.Load(ms, DataFormats.Rtf);
                }
            }
        }

        private void DeleteEntry_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedEntry(false);
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutDialog().ShowDialog();
        }

        private void Backup_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default["BackupOnExit"] = BackupOnExit.IsChecked;
            Properties.Settings.Default.Save();
        }

        #endregion
    }
}
