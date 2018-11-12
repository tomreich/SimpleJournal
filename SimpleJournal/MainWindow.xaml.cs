﻿/*
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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Ionic.Zip;
using System.IO;
using Microsoft.Win32;

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
        private async Task<bool> OpenJournalFile(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            string pass = TextPrompt.Prompt($"Enter Password for Journal {new FileInfo(filePath).Name}:", "Password?", string.Empty, TextPrompt.InputType.Password);
            if (string.IsNullOrWhiteSpace(pass))
                return false;

            // Put file names in list.
            LoadFileList(await GetFileNamesAsync(filePath, pass));

            _pw = pass;
            _journalFilePath = filePath;
            MenuItem_Save.IsEnabled = MenuItem_Delete.IsEnabled = MenuItem_NewEntry.IsEnabled = MenuItem_Revert.IsEnabled = MenuItem_SelectAllDates.IsEnabled = MenuItem_Find.IsEnabled = true;

            return true;
        }
        
        private void LoadFileList(List<string> allFiles, SelectedDatesCollection filter = null)
        {
            using (new WaitCursor())
            {
                // Put file names in list.
                fileListBox.Items.Clear();
                allFiles.Sort();
                allFiles.Reverse();
                _calendarReload = false;
                DateTime? filterDate = null;
                if (filter?.Any() == true)
                {
                    // Nulling the selected date will null out our filter, so save a copy.
                    // Fortunately we know there can only be one, so...
                    filterDate = filter[0];
                }
                entriesCalendar.SelectedDate = null;

                foreach (string fileName in allFiles)
                {
                    EntryFile entryFile = new EntryFile() { FileName = fileName };
                    if (entryFile.FileDateTime != DateTime.MinValue && (filterDate == null || filterDate.Value == entryFile.FileDate))
                    {
                        fileListBox.Items.Add(entryFile);
                        entriesCalendar.SelectedDates.Add(entryFile.FileDateTime);
                    }
                }
            }

            _calendarReload = true;
        }

        private async Task<List<string>> GetFileNamesAsync(string filePath, string pass)
        {
            List<string> fileNames = null;
            using (new WaitCursor())
            {
                await Task.Run(() =>
                {
                    using (ZipFile file = ZipFile.Read(filePath))
                    {
                        // Get the file names in the zip.
                        fileNames = GetFileNames(file);

                        if (fileNames?.Any() != true)
                            throw new Exception("No entries found in file!");

                        // Try to open one of the files to verify the password is correct.
                        using (MemoryStream ms = new MemoryStream())
                        {
                            try
                            {
                                file[0].ExtractWithPassword(ms, pass);
                            }
                            catch (BadPasswordException)
                            {
                                throw new Exception("Unable to load file.  Bad password?");
                            }
                        }
                    }
                });
            }
            return fileNames;
        }

        
        private List<string> GetFileNames(ZipFile file)
        {
            List<string> ret = new List<string>();
            file.Entries.ToList().ForEach(x => ret.Add(x.FileName));
            return ret;
        }
        

        private async Task<List<string>> GetFileNamesAsync(string fileName)
        {
            List<string> ret = new List<string>();
            await Task.Run(() =>
            {
                using (ZipFile file = ZipFile.Read(fileName))
                {
                    file.Entries.ToList().ForEach(x => ret.Add(x.FileName));
                }
            });
            return ret;
        }

        private async Task<bool> LoadFileFromZipAsync(string filePath, string fileName, string pw)
        {
            using (ZipFile file = ZipFile.Read(filePath))
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
                                await Task.Run(() => entry.ExtractWithPassword(ms, pw));
                            }
                            catch (BadPasswordException)
                            {
                                MessageBox.Show("Unable to load entry.  Bad password?", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                return false;
                            }

                            ms.Position = 0;
                            entryTextBox.IsEnabled = true;
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
            }
            return false;
        }

        private async Task<List<string>> SearchFilesInZip(ZipFile file, string term, string pw)
        {
            List<string> ret = new List<string>();
            foreach (ZipEntry entry in file)
            {
                // Load the entry
                using (MemoryStream ms = new MemoryStream())
                {
                    try
                    {
                        await Task.Run(() => entry.ExtractWithPassword(ms, pw));
                    }
                    catch (BadPasswordException)
                    {
                        MessageBox.Show("Unable to load entry.  Bad password?", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return ret;
                    }

                    ms.Position = 0;

                    // TODO: Find a way to strip the RTF formatting properly.  Formatted stuff might not be found.
                    StreamReader sr = new StreamReader(ms);
                    string entryContents = await sr.ReadToEndAsync();

                    if (entryContents.ToLower().Contains(term.ToLower()))
                        ret.Add(entry.FileName);
                }
            }
            return ret;
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
        private async void Calendar_SelectAll_Click(object sender, RoutedEventArgs e)
        {
            SaveSelectedEntry(false);
            _calendarReload = false;
            LoadFileList(await GetFileNamesAsync(_journalFilePath));
        }

        private async void Calendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_journalFilePath)) return;
            if (!_calendarReload) return;

            SaveSelectedEntry(false);
            LoadFileList(await GetFileNamesAsync(_journalFilePath), entriesCalendar.SelectedDates);
        }

        private async void fileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveSelectedEntry(false);

            if (fileListBox.SelectedValue != null)
            {
                _selectedFile = fileListBox.SelectedValue as EntryFile;

                if(await LoadFileFromZipAsync(_journalFilePath, _selectedFile.FileName, _pw))
                    TitleLabel.Content = _selectedFile.FileDateTime.ToString("dddd, MMMM d yyyy, hh:mm:ss tt");
            }
        }
        #endregion

        #region Form Load & Unload
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string defaultFile = Properties.Settings.Default["DefaultFile"].ToString();
            BackupOnExit.IsChecked = bool.Parse(Properties.Settings.Default["BackupOnExit"].ToString());
            if (!string.IsNullOrWhiteSpace(defaultFile) && File.Exists(defaultFile))
            {
                await OpenJournalFile(defaultFile);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveSelectedEntry(false);
            if(!string.IsNullOrWhiteSpace(_journalFilePath) && BackupOnExit.IsChecked)
            {
                // Save the backup file.
                FileInfo fi = new FileInfo(_journalFilePath);
                File.Copy(_journalFilePath, Path.Combine(fi.DirectoryName, fi.Name.Replace(fi.Extension, string.Empty) + "_" + DateTime.Now.ToString("s").Replace(':', '_') + fi.Extension));

                // Delete old backup files.
                var journalFiles = fi.Directory.GetFiles(fi.Name.Replace(fi.Extension, string.Empty) + "_*" + fi.Extension).OrderByDescending(x => x.CreationTime).ToList();
                while(journalFiles.Count > 3)
                {
                    // TODO: Can we do this async?
                    journalFiles[3].Delete();
                    journalFiles.RemoveAt(3);
                }
            }
        }
        #endregion

        #region Menubar Click Actions
        private async void Open_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog() { DefaultExt = ".zip", Filter = "Zip files (*.zip)|*.zip" };
            if (ofd.ShowDialog() == true)
            {
                _selectedFile = null;

                if (await OpenJournalFile(ofd.FileName))
                {
                    Properties.Settings.Default["DefaultFile"] = _journalFilePath;
                    Properties.Settings.Default.Save();
                }
            }
        }


        private async void NewEntry_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_journalFilePath)) return;

            entryTextBox.IsEnabled = true;

            SaveSelectedEntry(false);

            await AddNewEntry(_journalFilePath);
            List<string> fileNames = await GetFileNamesAsync(_journalFilePath, _pw);

            // Put file names in list.
            LoadFileList(fileNames);

            fileListBox.SelectedIndex = 0;
            FocusManager.SetFocusedElement(entryGrid, entryTextBox);
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveSelectedEntry(true);
        }

        private async void New_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog() { DefaultExt = ".zip", Filter = "Zip files (*.zip)|*.zip" };
            if (sfd.ShowDialog() == true)
            {
                _selectedFile = null;
                await AddNewEntry(sfd.FileName);
                await OpenJournalFile(sfd.FileName);
            }
        }

        private async Task AddNewEntry(string fileName)
        {
            using (new WaitCursor())
            {
                await Task.Run(() =>
                {
                    using (ZipFile file = new ZipFile(fileName))
                    {
                        file.AddEntry(DateTime.Now.ToString("s").Replace(':', '_') + ".rtf", "{\\rtf1}");
                        file.Save();
                    }
                });
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

        private async void Find_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_journalFilePath)) return;

            // Get the term to find.
            string term = TextPrompt.Prompt($"Find what?:", "Find", string.Empty, TextPrompt.InputType.Text);
            if (string.IsNullOrWhiteSpace(term))
                return;

            using (ZipFile file = ZipFile.Read(_journalFilePath))
            {
                List<string> filesWithTerm;
                using (new WaitCursor())
                    filesWithTerm = await SearchFilesInZip(file, term, _pw);

                if (filesWithTerm?.Any() != true)
                {
                    MessageBox.Show("No results found!", "Search", MessageBoxButton.OK, MessageBoxImage.Asterisk);
                    return;
                }

                MessageBox.Show($"Found {filesWithTerm.Count} files containing \"{term}\"!");

                // Put file names in list.
                LoadFileList(filesWithTerm);
            }
        }

        #endregion

        #region Wait Cursor
        // Blatantly stolen from Stack overflow
        // https://stackoverflow.com/questions/3480966/display-hourglass-when-application-is-busy
        public class WaitCursor : IDisposable
        {
            private Cursor _previousCursor;

            public WaitCursor()
            {
                _previousCursor = Mouse.OverrideCursor;

                Mouse.OverrideCursor = Cursors.Wait;
            }

            #region IDisposable Members

            public void Dispose()
            {
                Mouse.OverrideCursor = _previousCursor;
            }

            #endregion
        }
        #endregion
    }
}
