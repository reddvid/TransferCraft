﻿using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace TransferCraft
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string MC_SAVES_FOLDER = Environment.GetEnvironmentVariable("AppData") + @"\.minecraft\saves\";
        string ONEDRIVE_FOLDER = Environment.GetEnvironmentVariable("OneDrive") + @"\TransferCraft\";
        string TEMP_FOLDER = Environment.GetEnvironmentVariable("Temp");

        bool isBusy;
        string backupFileName;
        string tempFileName;
        string selectedSaveName;
        ZipOutputStream zipStream;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            BackupBtn.IsEnabled = false;

            // Check Minecraft saves directory
            string[] folders = Directory.GetDirectories(MC_SAVES_FOLDER);
            DirList.Items.Clear();

            if (folders.Length != 0)
            {
                foreach (var folder in folders)
                {
                    DirList.Items.Add(folder.Replace(MC_SAVES_FOLDER, string.Empty));
                }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (isBusy)
            {
                var result = MessageBox.Show("Backup still in progress. Are you sure you want to quit?\nAny unfinished backups may be corrupted or incomplete.", "Quit", MessageBoxButton.YesNo);

                if (result == MessageBoxResult.No) e.Cancel = true;
                else
                {
                    try
                    {
                        zipStream?.Close();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                    }
                    finally
                    {
                        File.Delete(tempFileName);
                        pBar.IsIndeterminate = isBusy = false;
                        e.Cancel = false;
                    }
                }
            }

            base.OnClosing(e);
        }

        private async Task ScanFiles(string filename)
        {
            tempFileName = $"{ TEMP_FOLDER }\\" + filename.Substring(filename.IndexOf(selectedSaveName.Trim() + "_"), filename.Length - filename.IndexOf(selectedSaveName.Trim() + "_"));

            var fsOut = File.Create(tempFileName);
            pBar.IsIndeterminate = isBusy = true;

            zipStream = new ZipOutputStream(fsOut);
            zipStream.SetLevel(5); //0-9, 9 being the highest level of compression
            int folderOffset = (MC_SAVES_FOLDER + selectedSaveName).Length + ((MC_SAVES_FOLDER + selectedSaveName).EndsWith("\\") ? 0 : 1);

            await Task.Run(() => CompressFolder((MC_SAVES_FOLDER + selectedSaveName), zipStream, folderOffset));

            zipStream.IsStreamOwner = true; // Makes the Close also Close the underlying stream
            zipStream?.Close();

            pBar.IsIndeterminate = isBusy = false;

            if (File.Exists(tempFileName)) File.Move(tempFileName, filename);

            MessageBox.Show("Export complete.");

            BackupBtn.Content = "Start Backup";
        }

        private void CompressFolder(string DATA_FOLDER, ZipOutputStream zipStream, int folderOffset)
        {
            string[] files = Directory.GetFiles(DATA_FOLDER, ".", SearchOption.AllDirectories);

            foreach (string filename in files)
            {
                try
                {
                    FileInfo fi = new FileInfo(filename);

                    string entryName = @"\" + selectedSaveName.Trim() + "\\" + filename.Substring(folderOffset); // Makes the name in zip based on the folder
                    entryName = ZipEntry.CleanName(entryName); // Removes drive from name and fixes slash direction
                    ZipEntry newEntry = new ZipEntry(entryName);
                    newEntry.DateTime = fi.LastWriteTime; // Note the zip format stores 2 second granularity

                    // Specifying the AESKeySize triggers AES encryption. Allowable values are 0 (off), 128 or 256.
                    // A password on the ZipOutputStream is required if using AES.
                    //   newEntry.AESKeySize = 256;

                    // To permit the zip to be unpacked by built-in extractor in WinXP and Server2003, WinZip 8, Java, and other older code,
                    // you need to do one of the following: Specify UseZip64.Off, or set the Size.
                    // If the file may be bigger than 4GB, or you do not need WinXP built-in compatibility, you do not need either,
                    // but the zip will be in Zip64 format which not all utilities can understand.
                    //   zipStream.UseZip64 = UseZip64.Off;
                    newEntry.Size = fi.Length;
                    zipStream.PutNextEntry(newEntry);
                    // Zip the file in buffered chunks
                    // the "using" will close the stream even if an exception occurs
                    byte[] buffer = new byte[4096];

                    using (FileStream streamReader = File.OpenRead(filename))
                    {
                        StreamUtils.Copy(streamReader, zipStream, buffer);
                    }
                    zipStream.CloseEntry();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }
        }

        private string GenerateFileName(string fileNameInitial, int count)
        {
            return Path.GetDirectoryName(fileNameInitial) +
                     Path.DirectorySeparatorChar +
                     Path.GetFileNameWithoutExtension(fileNameInitial) +
                     " (" + count.ToString() + ")" +
                     Path.GetExtension(fileNameInitial);
        }

        private async void Backup_Click(object sender, RoutedEventArgs e)
        {
            BackupBtn.Content = "Stop";

            if (isBusy)
            {               
                var result = MessageBox.Show("Are you sure you want to cancel the current backup?\nAny unfinished backups may be corrupted or incomplete.", "Stop", MessageBoxButton.YesNo);

                if (result == MessageBoxResult.No) return;
                else
                {
                    try
                    {
                        zipStream?.Close();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                    }
                    finally
                    {
                        File.Delete(tempFileName);
                        pBar.IsIndeterminate = isBusy = false;
                        BackupBtn.Content = "Start Backup";
                    }
                }
            }
            else
            {
                // Scan folder
                string path = MC_SAVES_FOLDER + selectedSaveName;
                if (Directory.Exists(path.Trim()))
                {
                    string fileNameInitial = $"{ ONEDRIVE_FOLDER }\\{ selectedSaveName }_{ Environment.MachineName }_{ DateTime.Now:MMddyy}.zip";
                    backupFileName = fileNameInitial;
                    int count = 1;

                    while (File.Exists(backupFileName))
                    {
                        backupFileName = GenerateFileName(fileNameInitial, count++);
                    }

                    try
                    {
                        if (Directory.Exists(ONEDRIVE_FOLDER))
                        {
                            await ScanFiles(backupFileName);
                        }
                        else
                        {
                            Directory.CreateDirectory(ONEDRIVE_FOLDER);
                            await ScanFiles(backupFileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                    }
                }
            }

          
        }

        private void OpenDrive_Click(object sender, RoutedEventArgs e)
        {
            var d = Environment.GetEnvironmentVariable("OneDrive") + @"\TransferCraft\";
            Process.Start("explorer.exe", d);
        }

        private void OpenSaves_Click(object sender, RoutedEventArgs e)
        {
            var d = Environment.GetEnvironmentVariable("AppData") + @"\.minecraft\saves\";
            Process.Start("explorer.exe", d);
        }

        private void DirList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace((string)e.AddedItems[0]))
            {
                selectedSaveName = e.AddedItems[0].ToString();
                BackupBtn.IsEnabled = true;
            }
        }
    }
}
