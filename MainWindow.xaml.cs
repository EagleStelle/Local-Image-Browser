using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;
using Windows.UI.Popups;
using Microsoft.UI.Xaml.Controls;

namespace App1
{
    public sealed partial class MainWindow : Window
    {
        private List<string> imageFiles;  // To hold the list of image files from the selected folder
        private int currentIndex;  // Track the current image index
        private string destinationFolder;  // Hold the destination folder path
        private MediaPlayer mediaPlayer;  // MediaPlayer for sound playback
        private FileSystemWatcher fileWatcher; // File watcher to monitor folder changes

        public MainWindow()
        {
            this.InitializeComponent();

            // Initialize FileSystemWatcher with default values
            fileWatcher = new FileSystemWatcher();
            fileWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
            fileWatcher.Filter = "*.*";
            fileWatcher.Changed += OnFolderChanged;
            fileWatcher.Created += OnFolderChanged;
            fileWatcher.Deleted += OnFolderChanged;
            fileWatcher.Renamed += OnFolderChanged;

            // Initialize hover events for Next and Previous buttons
            NextButton.Opacity = 0;
            PreviousButton.Opacity = 0;

            NextButton.PointerEntered += (s, e) => NextButton.Opacity = 1;
            NextButton.PointerExited += (s, e) => NextButton.Opacity = 0;

            PreviousButton.PointerEntered += (s, e) => PreviousButton.Opacity = 1;
            PreviousButton.PointerExited += (s, e) => PreviousButton.Opacity = 0;

            imageFiles = new List<string>();  // Initialize the image list
            currentIndex = -1;  // Initialize index to no selection
            destinationFolder = string.Empty;  // No folder selected initially
            mediaPlayer = new MediaPlayer();  // Initialize the MediaPlayer for sound

            // Ensure the window is centered after it's activated
            this.Activated += MainWindow_Activated;
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            // Unsubscribe the event to avoid multiple calls
            this.Activated -= MainWindow_Activated;

            CenterWindow();
        }
        private void CenterWindow()
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var centerX = (displayArea.WorkArea.Width - appWindow.Size.Width) / 2;
            var centerY = (displayArea.WorkArea.Height - appWindow.Size.Height) / 2;

            appWindow.Move(new PointInt32(centerX, centerY));
        }

        // Play sound using MediaPlayer
        private void PlaySound(string soundFilePath)
        {
            try
            {
                mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(soundFilePath));
                mediaPlayer.Play();
            }
            catch (Exception ex)
            {
                var dialog = new MessageDialog($"Error playing sound: {ex.Message}");
            }
        }

        // Event for the 'Browse Image Folder' button click
        private async void BrowseImageFolder_Click(object sender, RoutedEventArgs e)
        {
            FolderPicker folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);  // Get HWND for WinUI 3 compatibility
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                ImageFolderPath.Text = folder.Path;  // Update the Image Folder Path textbox
                LoadImagesFromFolder(folder.Path);  // Load all images from the selected folder
            }
        }

        // Event for the 'Browse Destination Folder' button click
        private async void BrowseDestinationFolder_Click(object sender, RoutedEventArgs e)
        {
            FolderPicker folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);  // Get HWND for WinUI 3 compatibility
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                DestinationFolderPath.Text = folder.Path;
                destinationFolder = folder.Path;  // Set the destination folder
            }
        }

        // This method is called when any file change occurs in the folder
        private void OnFolderChanged(object sender, FileSystemEventArgs e)
        {
            // Ensure UI updates are made on the main thread
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!string.IsNullOrEmpty(ImageFolderPath.Text))
                {
                    LoadImagesFromFolder(ImageFolderPath.Text);
                }
            });
        }

        // Load all image files from the selected folder
        private void LoadImagesFromFolder(string folderPath)
        {
            imageFiles = Directory.GetFiles(folderPath, "*.*")
                .Where(file => file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (imageFiles.Count > 0)
            {
                currentIndex = 0;  // Start with the first image
                DisplayImage(currentIndex);

                // Set the watcher to monitor the selected folder
                fileWatcher.Path = folderPath;
                fileWatcher.EnableRaisingEvents = true; // Enable the watcher
            }
            else
            {
                var dialog = new MessageDialog("No images found in the selected folder.");
                fileWatcher.EnableRaisingEvents = false; // Disable the watcher if no images
            }
        }

        // Event for the 'Next' button
        private void NextImage_Click(object sender, RoutedEventArgs e)
        {
            if (imageFiles.Count > 0)
            {
                ReleaseImageResources();  // Release current image resources
                currentIndex = (currentIndex + 1) % imageFiles.Count;  // Loop to the first image when reaching the end
                DisplayImage(currentIndex);
            }
        }

        // Event for the 'Previous' button
        private void PreviousImage_Click(object sender, RoutedEventArgs e)
        {
            if (imageFiles.Count > 0)
            {
                ReleaseImageResources();  // Release current image resources
                currentIndex = (currentIndex - 1 + imageFiles.Count) % imageFiles.Count;  // Loop to the last image when going before the first
                DisplayImage(currentIndex);
            }
        }

        // Method to display an image at the given index
        private void DisplayImage(int index)
        {
            if (index >= 0 && index < imageFiles.Count)
            {
                string selectedImagePath = imageFiles[index];

                // Display the file name on top
                ImageFileName.Text = Path.GetFileName(selectedImagePath);

                // Load the image into a MemoryStream to avoid file lock
                using (FileStream fs = new FileStream(selectedImagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.SetSource(fs.AsRandomAccessStream());

                    SelectedImage.Source = bitmap;  // Display the selected image
                }

                // Force garbage collection to clean up memory
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        // Event for the 'Move' button
        private async void MoveImage_Click(object sender, RoutedEventArgs e)
        {
            if (currentIndex >= 0 && currentIndex < imageFiles.Count)
            {
                if (!string.IsNullOrEmpty(destinationFolder))
                {
                    string sourceFile = imageFiles[currentIndex];
                    string fileName = Path.GetFileName(sourceFile);
                    string destinationPath = Path.Combine(destinationFolder, fileName);

                    try
                    {
                        // Release the image resources before moving
                        ReleaseImageResources();

                        // Now move the file
                        File.Move(sourceFile, destinationPath);
                        PlaySound("ms-appx:///Assets/Sounds/move.wav");

                        // Play a success sound (implementation dependent on available audio APIs)

                        // Remove the file from the list
                        imageFiles.RemoveAt(currentIndex);

                        if (imageFiles.Count > 0)
                        {
                            if (currentIndex >= imageFiles.Count) currentIndex = imageFiles.Count - 1;
                            DisplayImage(currentIndex);  // Display the next image
                        }
                        else
                        {
                            SelectedImage.Source = null;  // Clear the image display if no images left
                        }

                        // Force garbage collection after moving
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                    catch (IOException ex)
                    {
                        var dialog = new MessageDialog($"Error moving the image: {ex.Message}");
                        await dialog.ShowAsync();
                    }
                }
                else
                {
                    PlaySound("ms-appx:///Assets/Sounds/error.wav");
                }
            }
        }

        // Method to release resources used by the current image
        private void ReleaseImageResources()
        {
            if (SelectedImage.Source != null)
            {
                // Release the current image source to free up memory
                SelectedImage.Source = null;

                // Force garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        // Event handlers for mouse enter and leave events
        private void Button_MouseEnter(object sender, PointerRoutedEventArgs e)
        {
            var button = sender as Button;
            button.Visibility = Visibility.Visible;
        }

        private void Button_MouseLeave(object sender, PointerRoutedEventArgs e)
        {
            var button = sender as Button;
            button.Visibility = Visibility.Collapsed;
        }


        // Event for handling key presses (hotkeys)
        private void Grid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Left || e.Key == Windows.System.VirtualKey.A)
            {
                PreviousImage_Click(sender, e);  // Trigger Previous button when Left Arrow or 'A' is pressed
            }
            else if (e.Key == Windows.System.VirtualKey.Right || e.Key == Windows.System.VirtualKey.D)
            {
                NextImage_Click(sender, e);  // Trigger Next button when Right Arrow or 'D' is pressed
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.S)
            {
                MoveImage_Click(sender, e);  // Trigger Move button when Down Arrow or 'S' is pressed
            }
        }
    }
}
