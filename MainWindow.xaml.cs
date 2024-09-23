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
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Windows.UI.Core;

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
                    // Retry logic to wait for file access
                    const int maxRetries = 5;
                    const int delay = 500; // in milliseconds

                    for (int i = 0; i < maxRetries; i++)
                    {
                        try
                        {
                            LoadImagesFromFolder(ImageFolderPath.Text);
                            break;  // If successful, break out of the loop
                        }
                        catch (IOException)
                        {
                            Task.Delay(delay).Wait();  // Wait and retry
                        }
                    }
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
                ImageCount.Text = $"{currentIndex + 1} / {imageFiles.Count}";

                // Set the watcher to monitor the selected folder
                fileWatcher.Path = folderPath;
                fileWatcher.EnableRaisingEvents = true;
            }
            else
            {
                // No images found, reset to a blank state
                SelectedImage.Source = null;  // Clear the displayed image
                ImageFileName.Text = string.Empty;  // Clear the file name display
                ImageCount.Text = string.Empty;  // Clear image count display
                currentIndex = -1;  // Reset the index
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
                ImageCount.Text = $"{currentIndex + 1} / {imageFiles.Count}";
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
                ImageCount.Text = $"{currentIndex + 1} / {imageFiles.Count}";
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
                    string fileName = Path.GetFileNameWithoutExtension(sourceFile);
                    string fileExtension = Path.GetExtension(sourceFile);
                    string destinationPath = Path.Combine(destinationFolder, Path.GetFileName(sourceFile));

                    try
                    {
                        // Release the image resources before moving
                        ReleaseImageResources();

                        // Check for file name conflict and add suffix if necessary
                        int copyCount = 1;
                        while (File.Exists(destinationPath))
                        {
                            copyCount++;
                            destinationPath = Path.Combine(destinationFolder, $"{fileName} ({copyCount}){fileExtension}");
                        }

                        // Move the file to the destination
                        File.Move(sourceFile, destinationPath);
                        PlaySound("ms-appx:///Assets/Sounds/move.wav");

                        // Remove the file from the list and update the display
                        imageFiles.RemoveAt(currentIndex);

                        if (imageFiles.Count > 0)
                        {
                            if (currentIndex >= imageFiles.Count) currentIndex = imageFiles.Count - 1;
                            DisplayImage(currentIndex);
                        }
                        else
                        {
                            SelectedImage.Source = null; // Clear image if no images are left
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

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            if (ControlsContainer.Visibility == Visibility.Collapsed)
            {
                ControlsContainer.Visibility = Visibility.Visible;
            }
            else
            {
                ControlsContainer.Visibility = Visibility.Collapsed;
            }
        }

        // Event for double-clicking the ImageFileName (to enable renaming)
        private void ImageFileName_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (currentIndex >= 0 && currentIndex < imageFiles.Count)
            {
                // Get the full file path and file name
                string fullFileName = Path.GetFileName(imageFiles[currentIndex]);
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullFileName);  // Get name without extension

                // Show the filename (without the extension) in the RenameTextBox
                RenameTextBox.Text = fileNameWithoutExtension;
                RenameTextBox.Visibility = Visibility.Visible;  // Make the textbox visible for renaming
                RenameTextBox.Focus(FocusState.Programmatic);  // Focus on the textbox for input

                // Hide the ImageFileName TextBlock
                ImageFileName.Visibility = Visibility.Collapsed;
            }
        }

        // Event handler for pressing 'Enter' after renaming
        private void RenameTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter && currentIndex >= 0 && currentIndex < imageFiles.Count)
            {
                string newFileName = RenameTextBox.Text.Trim();  // Get the new name from the textbox
                if (!string.IsNullOrEmpty(newFileName))
                {
                    // Get the current file path and its extension
                    string currentFilePath = imageFiles[currentIndex];
                    string currentDirectory = Path.GetDirectoryName(currentFilePath);
                    string fileExtension = Path.GetExtension(currentFilePath);  // Get the file extension

                    // Combine the new file name with the existing extension
                    string newFilePath = Path.Combine(currentDirectory, newFileName + fileExtension);

                    try
                    {
                        // Rename the file on the file system
                        File.Move(currentFilePath, newFilePath);

                        // Update the image file list with the new file path
                        imageFiles[currentIndex] = newFilePath;

                        // Update the displayed file name and hide the RenameTextBox
                        ImageFileName.Text = Path.GetFileName(newFilePath);
                        RenameTextBox.Visibility = Visibility.Collapsed;
                    }
                    catch (IOException ex)
                    {
                        // Handle errors, like if the file already exists with the new name
                        var dialog = new MessageDialog($"Error renaming the file: {ex.Message}");
                    }
                }
                else
                {
                    // If no valid input is provided, hide the RenameTextBox and show ImageFileName again
                    RenameTextBox.Visibility = Visibility.Collapsed;
                    ImageFileName.Visibility = Visibility.Visible;
                }
            }
        }

        // Event handler if the RenameTextBox loses focus (optional but ensures renaming completes)
        private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Hide the RenameTextBox and show the ImageFileName TextBlock if the RenameTextBox loses focus
            RenameTextBox.Visibility = Visibility.Collapsed;
            ImageFileName.Visibility = Visibility.Visible;
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
            // Check if Alt is pressed using InputKeyboardSource
            var isCtrlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);

            // Arrow keys should work without Alt
            if (e.Key == Windows.System.VirtualKey.Left)
            {
                PreviousImage_Click(sender, e);  // Trigger Previous button when Left Arrow is pressed
            }
            else if (e.Key == Windows.System.VirtualKey.Right)
            {
                NextImage_Click(sender, e);  // Trigger Next button when Right Arrow is pressed
            }
            else if (e.Key == Windows.System.VirtualKey.Down)
            {
                MoveImage_Click(sender, e);  // Trigger Move button when Down Arrow is pressed
            }

            // A, S, D keys should only work with Alt
            else if (isCtrlPressed)
            {
                if (e.Key == Windows.System.VirtualKey.A)
                {
                    PreviousImage_Click(sender, e);  // Trigger Previous button when Alt + A is pressed
                }
                else if (e.Key == Windows.System.VirtualKey.D)
                {
                    NextImage_Click(sender, e);  // Trigger Next button when Alt + D is pressed
                }
                else if (e.Key == Windows.System.VirtualKey.S)
                {
                    MoveImage_Click(sender, e);  // Trigger Move button when Alt + S is pressed
                }
            }
        }
    }
}
