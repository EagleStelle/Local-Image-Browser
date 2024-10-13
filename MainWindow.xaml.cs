using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;
using Windows.UI.Popups;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Windows.UI.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Microsoft.UI.Xaml.Controls;
using ImageMagick;
using System.Diagnostics;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace App1
{
    public sealed partial class MainWindow : Window
    {
        // Hotkeys dictionary: Maps key combinations to actions
        private readonly Dictionary<(VirtualKey, bool), Action<object, KeyRoutedEventArgs>> keyMappings;

        // Image-related variables
        private List<string> imageFiles;   // Holds the list of image files from the selected folder
        private int currentIndex;          // Tracks the current image index
        private Dictionary<string, BitmapImage> imageCache = new Dictionary<string, BitmapImage>();  // Caches images for faster access

        // File and folder management
        private string destinationFolder;  // Stores the destination folder path
        private FileSystemWatcher fileWatcher; // Monitors the folder for any changes

        // Media playback
        private MediaPlayer mediaPlayer;   // Used for sound playback

        // Image zoom functionality
        private double _translateX = 0;    // Horizontal translation for zoomed image
        private double _translateY = 0;    // Vertical translation for zoomed image

        public MainWindow()
        {
            InitializeComponent();

            // Set up the title bar
            SetTitleBar(AppTitleBar);
            ExtendsContentIntoTitleBar = true;

            // Initialize hotkey mappings (Key, isCtrlPressed) => Action
            keyMappings = new Dictionary<(VirtualKey, bool), Action<object, KeyRoutedEventArgs>>
            {
                { (VirtualKey.X, true), (sender, e) => MoveImage_Click(sender, e) },                                                // Ctrl + X to move
                { (VirtualKey.Up, false), (sender, e) => MoveImage_Click(sender, e) },                                              // Up arrow to move
                { (VirtualKey.C, true), (sender, e) => CopyImage_Click(sender, e) },                                                // Ctrl + C to copy
                { (VirtualKey.Down, false), (sender, e) => CopyImage_Click(sender, e) },                                            // Down arrow to copy
                { (VirtualKey.Left, false), (sender, e) => PreviousImage_Click(sender, e) },                                        // Left arrow for previous image
                { (VirtualKey.Right, false), (sender, e) => NextImage_Click(sender, e) },                                           // Right arrow for next image
                { (VirtualKey.Left, true), (sender, e) => PreviousGrid_Click(sender, e) },                                          // Ctrl + Left arrow for previous gallery
                { (VirtualKey.Right, true), (sender, e) => NextGrid_Click(sender, e) },                                             // Ctrl + Right arrow for next gallery
                { (VirtualKey.Delete, false), (sender, e) => DeleteImage_Click(sender, e) },                                        // Delete key
                { (VirtualKey.I, true), (sender, e) => DirectoryButton_Click(sender, e)},                                           // Show Directory Panel
                { (VirtualKey.G, true), (sender, e) => GalleryButton_Click(sender, e)},                                           // Show Gallery Panel
                { (VirtualKey.R, true), (sender, e) => ConversionButton_Click(sender, e)},                                          // Show Conversion Panel
                { (VirtualKey.F2, false), (sender, e) => ImageFileName_DoubleTapped(sender, new DoubleTappedRoutedEventArgs()) },   // F2 to rename
                { (VirtualKey.F3, false), (sender, e) => ImageCount_DoubleTapped(sender, new DoubleTappedRoutedEventArgs()) },      // F3 to jump
                { (VirtualKey.F5, false), (sender, e) => ReloadUI() },                                                              // F5 to reload UI
            };

            // Initialize FileSystemWatcher to monitor folder changes
            fileWatcher = new FileSystemWatcher
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,  // Watch for file name and modification changes
                Filter = "*.*"  // Monitor all files
            };
            // Assign event handlers for folder changes
            fileWatcher.Changed += OnFolderChanged;
            fileWatcher.Created += OnFolderChanged;
            fileWatcher.Deleted += OnFolderChanged;
            fileWatcher.Renamed += OnFolderChanged;

            // Enable drag-and-drop functionality on the grid
            MainGrid.AllowDrop = true;
            MainGrid.Drop += OnDrop;
            MainGrid.DragOver += OnDragOver;

            // Set up hover effects for Next and Previous buttons
            NextButton.Opacity = 0;
            PreviousButton.Opacity = 0;
            NextButton.PointerEntered += (s, e) => NextButton.Opacity = 1;  // Show on hover
            NextButton.PointerExited += (s, e) => NextButton.Opacity = 0;   // Hide on exit
            PreviousButton.PointerEntered += (s, e) => PreviousButton.Opacity = 1;
            PreviousButton.PointerExited += (s, e) => PreviousButton.Opacity = 0;

            // Initialize image management variables
            imageFiles = new List<string>();  // Holds the list of image files
            currentIndex = -1;  // Set the index to indicate no image selected
            destinationFolder = string.Empty;  // No folder selected initially

            // Initialize MediaPlayer for sound effects
            mediaPlayer = new MediaPlayer();
        }

        // List of accepted image file extensions
        private readonly List<string> acceptedImageExtensions = new List<string>
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico", ".heic", ".svg"
        };
        // Handles the drop operation for both source and destination areas
        private async void OnDrop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0)
                {
                    var dropPosition = e.GetPosition(MainGrid).X;
                    var gridWidth = MainGrid.ActualWidth;
                    var storageItem = items[0];

                    // Left side of the UI (Source area)
                    if (dropPosition <= gridWidth / 2)
                    {
                        // If a folder is dropped, load images from the folder
                        if (storageItem.IsOfType(StorageItemTypes.Folder))
                        {
                            SourceFolderPath.Text = storageItem.Path; // Update source folder path
                            LoadImagesFromFolder(storageItem.Path);   // Load images from the folder
                        }
                        // If a file is dropped, ensure it's an image file before proceeding
                        else if (storageItem.IsOfType(StorageItemTypes.File))
                        {
                            var fileExtension = Path.GetExtension(storageItem.Name).ToLower();
                            if (acceptedImageExtensions.Contains(fileExtension))
                            {
                                var folderPath = Path.GetDirectoryName(storageItem.Path);
                                SourceFolderPath.Text = folderPath; // Update the folder path
                                LoadImagesFromFolder(folderPath);   // Load images from the folder

                                // Display the dropped image
                                var imagePath = storageItem.Path;
                                currentIndex = imageFiles.IndexOf(imagePath); // Update current index
                                if (currentIndex != -1)
                                {
                                    DisplayImage(currentIndex);
                                    ImageCount.Text = $"{currentIndex + 1} / {imageFiles.Count}"; // Show image count
                                }
                            }
                        }

                    }
                    // Right side of the UI (Destination area)
                    else
                    {
                        // If a folder is dropped, update the destination folder
                        if (storageItem.IsOfType(StorageItemTypes.Folder))
                        {
                            destinationFolder = storageItem.Path; // Set destination folder
                            DestinationFolderPath.Text = destinationFolder; // Update the displayed path
                        }
                        // If an image file is dropped, set its folder as the destination
                        else if (storageItem.IsOfType(StorageItemTypes.File))
                        {
                            var folderPath = Path.GetDirectoryName(storageItem.Path);
                            destinationFolder = folderPath; // Set destination folder
                            DestinationFolderPath.Text = destinationFolder; // Update the displayed path
                        }
                    }
                }
            }
        }
        // Provides visual feedback during drag-over operation
        private void OnDragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy; // Allows the copy operation
            var dropPosition = e.GetPosition(MainGrid).X;
            var gridWidth = MainGrid.ActualWidth;

            // Display "Drop for Source" or "Drop for Destination" based on the drag position
            e.DragUIOverride.Caption = dropPosition <= gridWidth / 2 ? "Drop for Source" : "Drop for Destination";
            e.DragUIOverride.IsCaptionVisible = true; // Show caption
            e.DragUIOverride.IsContentVisible = true; // Show content
        }

        // Loads images from the specified folder and updates UI elements accordingly
        private async void LoadImagesFromFolder(string folderPath)
        {
            // If the folder path has changed, release resources before loading new images
            if (SourceFolderPath.Text != folderPath)
            {
                await ReleaseImageResources(); // Free up resources
            }

            // Load all files with the accepted image extensions from the folder
            imageFiles = Directory.GetFiles(folderPath, "*.*")
                .Where(file => acceptedImageExtensions.Contains(Path.GetExtension(file).ToLower()))
                .OrderBy(file => new System.Text.RegularExpressions.Regex(@"\d+").Matches(Path.GetFileNameWithoutExtension(file))
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(m =>
                    {
                        if (long.TryParse(m.Value, out long num))
                        {
                            return num;
                        }
                        return 0;
                    })
                    .DefaultIfEmpty(0)
                    .First())
                .ThenBy(file => file)
                .ToList();

            LoadPage(0); // Load the first page of images

            // Update the UI with the first image and image count if images are found
            if (imageFiles.Count > 0)
            {
                currentIndex = 0;
                DisplayImage(currentIndex);
                ImageCount.Text = $"{currentIndex + 1} / {imageFiles.Count}"; // Display total image count

                // Set up file watcher to monitor folder changes
                fileWatcher.Path = folderPath;
                fileWatcher.EnableRaisingEvents = true;
            }
            else
            {
                // Clear the UI if no images are found
                SelectedImage.Source = null;
                ImageFileName.Text = string.Empty;
                ImageResolution.Text = string.Empty;
                ImageCount.Text = string.Empty;
                currentIndex = -1;
            }
        }

        // This method is called when any file change occurs in the folder
        private void OnFolderChanged(object sender, FileSystemEventArgs e)
        {
            // Ensure UI updates are made on the main thread
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!string.IsNullOrEmpty(SourceFolderPath.Text))
                {
                    // Retry logic to wait for file access
                    const int maxRetries = 5;
                    const int delay = 500; // in milliseconds

                    for (int i = 0; i < maxRetries; i++)
                    {
                        try
                        {
                            LoadImagesFromFolder(SourceFolderPath.Text);
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

        // Grid view gallery
        private const int PageSize = 10;
        private int currentPage = 0;

        // Method to load a specific page of images
        private void LoadPage(int pageIndex)
        {
            currentPage = pageIndex;
            var pageItems = imageFiles.Skip(pageIndex * PageSize).Take(PageSize).ToList();
            ImageGridView.ItemsSource = pageItems.Select(f => new BitmapImage(new Uri(f))).ToList();
        }

        private void ImageGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImageGridView.SelectedItem != null)
            {
                // Calculate global index based on current page and the selected index within that page
                currentIndex = (currentPage * PageSize) + ImageGridView.SelectedIndex;

                // Display the selected image in the main viewer
                DisplayImage(currentIndex);

                // Update the image count to show the global index
                ImageCount.Text = $"{currentIndex + 1} / {imageFiles.Count}";
            }
        }
        // Adjust Next and Previous buttons to handle paging
        private void NextImage_Click(object sender, RoutedEventArgs e)
        {
            ReleaseCurrentResources();

            if (imageFiles.Count > 0)
            {
                // Increment index and loop around if at the end
                currentIndex = (currentIndex + 1) % imageFiles.Count;

                // Update the selection in the GridView and display the new image
                UpdateSelection();  // This updates both the GridView selection and the displayed image
            }
        }
        private void PreviousImage_Click(object sender, RoutedEventArgs e)
        {
            ReleaseCurrentResources();

            if (imageFiles.Count > 0)
            {
                // Decrement index and loop around if at the beginning
                currentIndex = (currentIndex - 1 + imageFiles.Count) % imageFiles.Count;

                // Update the selection in the GridView and display the new image
                UpdateSelection();  // This updates both the GridView selection and the displayed image
            }
        }
        private void UpdateSelection()
        {
            // Load the appropriate page based on the current index
            int pageIndex = currentIndex / PageSize;
            LoadPage(pageIndex);

            // Select the correct image in the GridView
            ImageGridView.SelectedIndex = currentIndex % PageSize;

            // Update the image count display
            ImageCount.Text = $"{currentIndex + 1} / {imageFiles.Count}";

            // Ensure the new image is displayed in the main viewer
            DisplayImage(currentIndex);
        }

        // Method to toggle visibility of the containers based on the selected one
        private void ToggleContainerVisibility(FrameworkElement selectedContainer)
        {
            // Collapse all containers except the selected one
            if (selectedContainer.Visibility == Visibility.Collapsed)
            {
                DirectoryContainer.Visibility = Visibility.Collapsed;
                GalleryContainer.Visibility = Visibility.Collapsed;
                ConversionContainer.Visibility = Visibility.Collapsed;

                // Show the selected container
                selectedContainer.Visibility = Visibility.Visible;
            }
            else
            {
                // Collapse the selected container if it is already visible
                selectedContainer.Visibility = Visibility.Collapsed;
            }
        }
        // Event handler for the Directory button
        private void DirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleContainerVisibility(DirectoryContainer);
        }
        // Event handler for the Gallery button
        private void GalleryButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleContainerVisibility(GalleryContainer);
        }
        // Event handler for the Conversion button
        private void ConversionButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleContainerVisibility(ConversionContainer);
        }


        private void NextGrid_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to the next set of images with circular navigation
            currentIndex = (currentIndex + PageSize) % imageFiles.Count;

            // Update the current page based on the new index
            currentPage = currentIndex / PageSize;

            // Load the images for the new page
            LoadPage(currentPage);

            // Jump to the first image in the GridView of the next page
            ImageGridView.SelectedIndex = 0;
            currentIndex = currentPage * PageSize;

            // Update the display and image count
            DisplayImage(currentIndex);
            ImageCount.Text = $"{currentIndex + 1} / {imageFiles.Count}";
        }
        private void PreviousGrid_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to the previous set of images with circular navigation
            currentIndex = (currentIndex - PageSize + imageFiles.Count) % imageFiles.Count;

            // Update the current page based on the new index
            currentPage = currentIndex / PageSize;

            // Load the images for the new page
            LoadPage(currentPage);

            // Determine the number of items on the current page
            var itemsOnCurrentPage = imageFiles.Skip(currentPage * PageSize).Take(PageSize).Count();

            // Jump to the last image in the GridView of the previous page
            ImageGridView.SelectedIndex = itemsOnCurrentPage - 1; // Select the last item on the page
            currentIndex = (currentPage * PageSize) + ImageGridView.SelectedIndex;

            // Update the display and image count
            DisplayImage(currentIndex);
            ImageCount.Text = $"{currentIndex + 1} / {imageFiles.Count}";
        }

        // Method to display an image at the given index
        private CompositeTransform imageTransform = new CompositeTransform();


        // Method to display an image at the given index (unchanged logic)
        private void DisplayImage(int currentIndex)
        {
            ReleaseCurrentResources(); // Release resources before loading a new image

            if (LoadTypeToggle.IsOn)
            {
                _ = DisplayImageAsync(currentIndex);
            }
            else
            {
                DisplayImageSync(currentIndex);
            }
        }

        // Sync method
        private void DisplayImageSync(int index)
        {
            if (index >= 0 && index < imageFiles.Count)
            {
                string selectedImagePath = imageFiles[index];
                ImageFileName.Text = Path.GetFileName(selectedImagePath);

                using (FileStream fs = new FileStream(selectedImagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.SetSource(fs.AsRandomAccessStream());

                    SelectedImage.Source = bitmap;

                    // Display the image resolution
                    ImageResolution.Text = $"({bitmap.PixelWidth} x {bitmap.PixelHeight})";
                }

                ImageCount.Text = $"{index + 1} / {imageFiles.Count}";
            }
        }

        // Async method (Added exception handling)
        private async Task DisplayImageAsync(int index)
        {
            if (index >= 0 && index < imageFiles.Count)
            {
                try
                {
                    string selectedImagePath = imageFiles[index];
                    ImageFileName.Text = Path.GetFileName(selectedImagePath);

                    BitmapImage bitmap = new BitmapImage();

                    bitmap.ImageOpened += (s, e) =>
                    {
                        // Ensure this code runs on the UI thread
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            SelectedImage.Source = bitmap;
                            // Display the image resolution
                            ImageResolution.Text = $"({bitmap.PixelWidth} x {bitmap.PixelHeight})";
                        });
                    };

                    // Open file stream for reading the image
                    using (FileStream fs = new FileStream(selectedImagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        // Set the image source using a random access stream
                        await bitmap.SetSourceAsync(fs.AsRandomAccessStream());
                    }

                    // Update image count label on the UI thread
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ImageCount.Text = $"{index + 1} / {imageFiles.Count}";
                    });
                }
                catch (Exception ex)
                {
                    // Handle exceptions (e.g., file access issues)
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ImageResolution.Text = $"Error loading image: {ex.Message}";
                        ImageFileName.Text = "N/A";
                        ImageCount.Text = "N/A";
                    });
                }
            }
        }

        private Point _zoomCenter; // This will store the current zoom focal point
        // Zoom using mouse scroll wheel, zoom to cursor position
        private void ScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(scrollViewer).Properties.MouseWheelDelta;
            var pointerPosition = e.GetCurrentPoint(SelectedImage).Position;

            // Store the cursor position as the zoom focal point
            _zoomCenter = new Point(pointerPosition.X, pointerPosition.Y);

            if (delta > 0 && ZoomSlider.Value < ZoomSlider.Maximum)
            {
                ZoomSlider.Value += 0.1;
            }
            else if (delta < 0 && ZoomSlider.Value > ZoomSlider.Minimum)
            {
                ZoomSlider.Value -= 0.1;
            }

            // Adjust translations to zoom towards the cursor position
            if (compositeTransform != null)
            {
                double zoomFactor = ZoomSlider.Value;
                ApplyZoom(zoomFactor, _zoomCenter);
            }
        }

        // Apply zoom relative to the focal point (shared for both mouse and slider zoom)
        private void ApplyZoom(double zoomFactor, Point zoomCenter)
        {
            var relativeX = zoomCenter.X - scrollViewer.ViewportWidth / 2;
            var relativeY = zoomCenter.Y - scrollViewer.ViewportHeight / 2;

            _translateX -= relativeX * (zoomFactor - compositeTransform.ScaleX);
            _translateY -= relativeY * (zoomFactor - compositeTransform.ScaleY);

            // Apply scaling and translations
            compositeTransform.ScaleX = zoomFactor;
            compositeTransform.ScaleY = zoomFactor;
            compositeTransform.TranslateX = _translateX;
            compositeTransform.TranslateY = _translateY;

            ApplyBoundaryConstraints();
        }

        // Handle slider value change
        private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (compositeTransform != null)
            {
                double newZoomFactor = e.NewValue;

                // Use the last focal point (whether set by mouse or default center)
                if (_zoomCenter == default(Point))
                {
                    _zoomCenter = new Point(scrollViewer.ViewportWidth / 2, scrollViewer.ViewportHeight / 2); // default to center
                }

                ApplyZoom(newZoomFactor, _zoomCenter);

                // Update the zoom percentage text, if the TextBlock exists
                if (ZoomPercentageText != null)
                {
                    ZoomPercentageText.Text = $"{(newZoomFactor * 100).ToString("F0")}%";
                }

                if (newZoomFactor == 1)
                {
                    // Reset translations when zoom is 1 (100%)
                    _translateX = 0;
                    _translateY = 0;
                    compositeTransform.TranslateX = 0;
                    compositeTransform.TranslateY = 0;
                }
            }
        }
        // Drag the image when zoomed in (without sliding and limited to boundaries)
        private void ScrollViewer_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (compositeTransform.ScaleX > 1 || compositeTransform.ScaleY > 1)
            {
                _translateX += e.Delta.Translation.X;
                _translateY += e.Delta.Translation.Y;

                // Apply boundary constraints after dragging
                ApplyBoundaryConstraints();
            }
        }
        // Ensure the image can't be dragged outside its boundaries
        private void ApplyBoundaryConstraints()
        {
            var imageWidth = SelectedImage.ActualWidth * compositeTransform.ScaleX;
            var imageHeight = SelectedImage.ActualHeight * compositeTransform.ScaleY;

            var viewportWidth = scrollViewer.ViewportWidth;
            var viewportHeight = scrollViewer.ViewportHeight;

            // Horizontal constraints
            if (imageWidth > viewportWidth)
            {
                var maxX = (imageWidth - viewportWidth) / 2;
                if (_translateX > maxX) _translateX = maxX;
                if (_translateX < -maxX) _translateX = -maxX;
            }
            else
            {
                _translateX = 0;  // Center the image if it's smaller than the viewport
            }

            // Vertical constraints
            if (imageHeight > viewportHeight)
            {
                var maxY = (imageHeight - viewportHeight) / 2;
                if (_translateY > maxY) _translateY = maxY;
                if (_translateY < -maxY) _translateY = -maxY;
            }
            else
            {
                _translateY = 0;  // Center the image if it's smaller than the viewport
            }

            // Apply constrained translations
            compositeTransform.TranslateX = _translateX;
            compositeTransform.TranslateY = _translateY;
        }

        // Methods for Directory
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
                SourceFolderPath.Text = folder.Path;  // Update the Image Folder Path textbox
                LoadImagesFromFolder(folder.Path);    // Load all images from the selected folder

                // Enable the SwitchFolders button if both source and destination are filled
                UpdateSwitchFoldersButtonState();
            }
        }
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

                // Enable the SwitchFolders button if both source and destination are filled
                UpdateSwitchFoldersButtonState();
            }
        }

        // Function to update the state of the SwitchFolders button
        private void UpdateSwitchFoldersButtonState()
        {
            // Enable the SwitchFolders button if both source and destination folders are selected
            SwitchFolders.IsEnabled = !string.IsNullOrEmpty(SourceFolderPath.Text) && !string.IsNullOrEmpty(DestinationFolderPath.Text);
        }
        private void SwitchFolders_Click(object sender, RoutedEventArgs e)
        {
            // Swap the paths of the source and destination folders
            string temp = SourceFolderPath.Text;
            SourceFolderPath.Text = DestinationFolderPath.Text;
            DestinationFolderPath.Text = temp;

            // Update the destination folder variable
            destinationFolder = DestinationFolderPath.Text;

            // Check if the new source folder (previous destination folder) is empty
            if (!string.IsNullOrEmpty(SourceFolderPath.Text) && Directory.Exists(SourceFolderPath.Text))
            {
                var files = Directory.GetFiles(SourceFolderPath.Text);
                if (files.Length == 0)
                {
                    // If the folder is empty, clear the imageFiles list and other UI elements
                    imageFiles.Clear();
                    SelectedImage.Source = null;
                    ImageFileName.Text = string.Empty;
                    ImageCount.Text = string.Empty;
                    currentIndex = -1;

                    // Disable the file watcher since there is no folder to watch
                    fileWatcher.EnableRaisingEvents = false;
                }
                else
                {
                    // Update the FileSystemWatcher to monitor the new source folder if it's not empty
                    fileWatcher.EnableRaisingEvents = false;  // Disable the watcher to avoid conflicts during switching
                    fileWatcher.Path = SourceFolderPath.Text;
                    fileWatcher.EnableRaisingEvents = true;  // Re-enable the watcher

                    // Reload the images from the new source folder
                    LoadImagesFromFolder(SourceFolderPath.Text);
                }
            }
        }
        // Methods for Conversion

        private async void ConvertImage_Click(object sender, RoutedEventArgs e)
        {
            if (currentIndex < 0 || currentIndex >= imageFiles.Count)
            {
                await ShowMessage("Please select an image first.");
                return;
            }

            // Check if the toggle button is on for bulk conversion
            if (BulkConvertToggle.IsOn)
            {
                ComboBoxItem selectedSourceFormatItem = (ComboBoxItem)SourceExtensionComboBox.SelectedItem;
                string sourceExtension = selectedSourceFormatItem?.Tag?.ToString();

                ComboBoxItem selectedOutputFormatItem = (ComboBoxItem)OutputExtensionComboBox.SelectedItem;
                string outputExtension = selectedOutputFormatItem?.Tag?.ToString();

                if (string.IsNullOrEmpty(sourceExtension) || string.IsNullOrEmpty(outputExtension))
                {
                    await ShowMessage("Please select both source and target formats.");
                    return;
                }

                // Filter image files by the source extension and convert them to the output extension
                var tasks = imageFiles
                    .Where(imagePath => Path.GetExtension(imagePath).Equals(sourceExtension, StringComparison.OrdinalIgnoreCase))
                    .Select(imagePath => ConvertImageToFormat(imagePath, outputExtension));

                try
                {
                    await Task.WhenAll(tasks);
                    await ShowMessage($"All images with {sourceExtension.ToUpper()} extension have been converted to {outputExtension.ToUpper()}.");
                }
                catch (Exception ex)
                {
                    await ShowMessage($"Error during bulk conversion: {ex.Message}");
                }
            }
            else
            {
                // Convert only the currently displayed image
                string selectedImagePath = imageFiles[currentIndex];
                ComboBoxItem selectedFormat = (ComboBoxItem)OutputExtensionComboBox.SelectedItem;
                string selectedExtension = selectedFormat?.Tag?.ToString();

                if (string.IsNullOrEmpty(selectedExtension))
                {
                    await ShowMessage("Please select a format to convert the image.");
                    return;
                }

                try
                {
                    await ConvertImageToFormat(selectedImagePath, selectedExtension);
                    await ShowMessage("Image converted successfully.");
                }
                catch (Exception ex)
                {
                    await ShowMessage($"Error converting image: {ex.Message}");
                }
            }

            // Refresh and display the updated image list
            int previousIndex = currentIndex;
            string previousImagePath = imageFiles[currentIndex];
            LoadImagesFromFolder(SourceFolderPath.Text);

            currentIndex = imageFiles.IndexOf(previousImagePath);
            if (currentIndex == -1)
            {
                currentIndex = Math.Min(previousIndex, imageFiles.Count - 1);
            }
            DisplayImage(currentIndex);
        }

        // Helper method to perform the actual conversion
        private async Task ConvertImageToFormat(string imagePath, string selectedExtension)
        {
            string outputFolder;
            if (SaveToSameFolderToggle.IsOn)
            {
                outputFolder = Path.GetDirectoryName(imagePath);
            }
            else
            {
                if (string.IsNullOrEmpty(destinationFolder) || !Directory.Exists(destinationFolder))
                {
                    await ShowMessage("Please specify a valid destination folder.");
                    return;
                }
                outputFolder = destinationFolder;
            }

            string outputFilePath = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(imagePath) + selectedExtension);

            try
            {
                ReleaseCurrentResources();
                fileWatcher.EnableRaisingEvents = false;

                await Task.Run(() =>
                {
                    using (MagickImage image = new MagickImage(imagePath))
                    {
                        switch (selectedExtension)
                        {
                            case ".png": image.Format = MagickFormat.Png; break;
                            case ".jpg": image.Format = MagickFormat.Jpg; break;
                            case ".jpeg": image.Format = MagickFormat.Jpeg; break;
                            case ".bmp": image.Format = MagickFormat.Bmp; break;
                            case ".gif": image.Format = MagickFormat.Gif; break;
                            case ".webp": image.Format = MagickFormat.WebP; break;
                            case ".ico":
                                int maxIcoSize = 128;
                                if (image.Width > maxIcoSize || image.Height > maxIcoSize)
                                {
                                    image.Resize((uint)maxIcoSize, (uint)maxIcoSize);
                                }
                                image.Format = MagickFormat.Icon;
                                break;
                            default:
                                throw new NotSupportedException($"The selected format {selectedExtension} is not supported.");
                        }
                        image.Write(outputFilePath);
                    }
                });
            }
            catch (Exception ex)
            {
                await ShowMessage($"Error converting image: {ex.Message}");
            }
            finally
            {
                fileWatcher.EnableRaisingEvents = true;
            }
        }

        // Method for Move
        private async void MoveImage_Click(object sender, RoutedEventArgs e)
        {
            if (currentIndex >= 0 && currentIndex < imageFiles.Count)
            {
                if (!string.IsNullOrEmpty(destinationFolder))
                {
                    string sourceFile = imageFiles[currentIndex];
                    string sourceDirectory = Path.GetDirectoryName(sourceFile);
                    string fileName = Path.GetFileNameWithoutExtension(sourceFile);
                    string fileExtension = Path.GetExtension(sourceFile);
                    string destinationPath = Path.Combine(destinationFolder, Path.GetFileName(sourceFile));

                    try
                    {
                        // Release the image resources before moving
                        ReleaseCurrentResources();

                        // Disable FileSystemWatcher before moving the file
                        fileWatcher.EnableRaisingEvents = false;

                        // Check if the source and destination folders are the same
                        if (!sourceDirectory.Equals(destinationFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            // Check for file name conflict and add suffix if necessary
                            int copyCount = 1;
                            while (File.Exists(destinationPath))
                            {
                                copyCount++;
                                destinationPath = Path.Combine(destinationFolder, $"{fileName} ({copyCount}){fileExtension}");
                            }

                            // Move the file to the destination
                            File.Move(sourceFile, destinationPath);

                            // Update the image list after moving the file
                            imageFiles.RemoveAt(currentIndex);  // Remove the moved image from the source list

                            // Adjust the index to avoid resetting to the first image
                            if (imageFiles.Count > 0)
                            {
                                currentIndex = Math.Min(currentIndex, imageFiles.Count - 1);  // Adjust the index to stay within bounds
                                DisplayImage(currentIndex);  // Display the next image or the last one
                            }
                            else
                            {
                                // No images left, reset to a blank state
                                SelectedImage.Source = null;
                                ImageFileName.Text = string.Empty;
                                ImageCount.Text = string.Empty;
                                currentIndex = -1;
                            }
                        }
                        else
                        {
                            DisplayImage(currentIndex);  // Display the current image if it's not moved
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
                    finally
                    {
                        // Re-enable FileSystemWatcher after moving the file
                        fileWatcher.EnableRaisingEvents = true;
                    }
                }
                else
                {

                }
            }
        }
        // Method for Copy
        private async void CopyImage_Click(object sender, RoutedEventArgs e)
        {
            if (currentIndex >= 0 && currentIndex < imageFiles.Count)
            {
                if (!string.IsNullOrEmpty(destinationFolder))
                {
                    string sourceFile = imageFiles[currentIndex];
                    string sourceDirectory = Path.GetDirectoryName(sourceFile);
                    string fileName = Path.GetFileNameWithoutExtension(sourceFile);
                    string fileExtension = Path.GetExtension(sourceFile);
                    string destinationPath = Path.Combine(destinationFolder, Path.GetFileName(sourceFile));

                    try
                    {
                        // Check if the source and destination folders are the same
                        if (!sourceDirectory.Equals(destinationFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            // Check for file name conflict and add suffix if necessary
                            int copyCount = 1;
                            while (File.Exists(destinationPath))
                            {
                                copyCount++;
                                destinationPath = Path.Combine(destinationFolder, $"{fileName} ({copyCount}){fileExtension}");
                            }

                            // Copy the file to the destination
                            File.Copy(sourceFile, destinationPath);

                            // Keep the image displayed in the UI (no resource release)
                        }
                        else
                        {
                            DisplayImage(currentIndex);  // Display the current image if it's not copied
                        }

                        // Force garbage collection after copying
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                    catch (IOException ex)
                    {
                        var dialog = new MessageDialog($"Error copying the image: {ex.Message}");
                        await dialog.ShowAsync();
                    }
                }
                else
                {
                    // Failed
                }
            }
        }
        // Method for Delete
        private async void DeleteImage_Click(object sender, RoutedEventArgs e)
        {
            if (currentIndex >= 0 && currentIndex < imageFiles.Count)
            {
                string fileToDelete = imageFiles[currentIndex];

                // Create the confirmation dialog
                var confirmDialog = new ContentDialog
                {
                    Title = "Delete Confirmation",
                    Content = $"Are you sure you want to delete {Path.GetFileName(fileToDelete)}?",
                    PrimaryButtonText = "Yes",
                    CloseButtonText = "No",
                    XamlRoot = this.Content.XamlRoot  // Set XamlRoot for proper dialog display
                };

                // Show the dialog and capture the result
                var result = await confirmDialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    try
                    {
                        // Release image resources before deleting
                        ReleaseCurrentResources();

                        // Save the current index and image path
                        int previousIndex = currentIndex;
                        string previousImagePath = imageFiles[currentIndex];

                        // Delete the file
                        File.Delete(fileToDelete);

                        // Remove the image from the list
                        imageFiles.RemoveAt(currentIndex);

                        // Check if there are still images in the folder
                        if (imageFiles.Count > 0)
                        {
                            // Adjust index to avoid out of bounds
                            currentIndex = Math.Min(previousIndex, imageFiles.Count - 1);

                            // Refresh the folder contents to ensure up-to-date state
                            LoadImagesFromFolder(SourceFolderPath.Text);

                            // Display the next or nearest image
                            DisplayImage(currentIndex);
                        }
                        else
                        {
                            // No images left, reset the display
                            SelectedImage.Source = null;
                            ImageFileName.Text = string.Empty;
                            ImageCount.Text = string.Empty;
                            currentIndex = -1;
                        }

                        // Force garbage collection after deleting
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                    catch (IOException ex)
                    {
                        // Show error message if deletion fails
                        var errorDialog = new ContentDialog
                        {
                            Title = "Error",
                            Content = $"Error deleting the image: {ex.Message}",
                            CloseButtonText = "Ok",
                            XamlRoot = this.Content.XamlRoot
                        };

                        await errorDialog.ShowAsync();
                    }
                }
            }
        }

        private void BulkConvert_Toggled(object sender, RoutedEventArgs e)
        {
            // Check if BulkConvertToggleButton is switched on
            if (BulkConvertToggle.IsOn)
            {
                // Make SourceFormatTextBlock and SourceExtensionComboBox visible
                SourceFormatTextBlock.Visibility = Visibility.Visible;
                SourceExtensionComboBox.Visibility = Visibility.Visible;
            }
            else
            {
                // Hide SourceFormatTextBlock and SourceExtensionComboBox
                SourceFormatTextBlock.Visibility = Visibility.Collapsed;
                SourceExtensionComboBox.Visibility = Visibility.Collapsed;
            }
        }


        // Event for double-clicking
        private void ImageCount_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (imageFiles.Count > 0)
            {
                // Set the TextBox to the current image index (1-based index)
                JumpToTextBox.Text = (currentIndex + 1).ToString();

                // Show the TextBox and hide the ImageCount TextBlock
                JumpToTextBox.Visibility = Visibility.Visible;
                JumpToTextBox.Focus(FocusState.Programmatic);  // Focus the TextBox for input
                ImageCount.Visibility = Visibility.Collapsed;
            }
        }
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

        // ImageCount TextBox
        private void JumpToTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                // Get the entered value
                if (int.TryParse(JumpToTextBox.Text.Trim(), out int targetIndex))
                {
                    // Convert to 0-based index
                    targetIndex -= 1;

                    // Check if the target index is within the valid range
                    if (targetIndex >= 0 && targetIndex < imageFiles.Count)
                    {
                        // Update the current index and display the corresponding image
                        ReleaseCurrentResources();
                        currentIndex = targetIndex;
                        DisplayImage(currentIndex);

                        // Update the ImageCount TextBlock
                        ImageCount.Text = $"{currentIndex + 1} / {imageFiles.Count}";
                    }
                    else
                    {
                        // Failed
                    }
                }

                // Hide the TextBox and show the ImageCount TextBlock
                JumpToTextBox.Visibility = Visibility.Collapsed;
                ImageCount.Visibility = Visibility.Visible;
            }
        }
        private void JumpToTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Hide the RenameTextBox and show the ImageFileName TextBlock if the RenameTextBox loses focus
            JumpToTextBox.Visibility = Visibility.Collapsed;
            ImageCount.Visibility = Visibility.Visible;
        }

        // ImageFileName TextBox
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
        private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Hide the RenameTextBox and show the ImageFileName TextBlock if the RenameTextBox loses focus
            RenameTextBox.Visibility = Visibility.Collapsed;
            ImageFileName.Visibility = Visibility.Visible;
        }

        // Memory Management
        private async Task ReleaseImageResources()
        {
            // Perform release and garbage collection asynchronously
            await Task.Run(() =>
            {
                // Clear the image currently being shown
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (SelectedImage.Source is BitmapImage bitmapImage)
                    {
                        bitmapImage.UriSource = null;  // Clear the image source
                        SelectedImage.Source = null;   // Set the image to null
                    }
                });

                // Clear GridView items (also holds BitmapImage objects)
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ImageGridView.ItemsSource is IEnumerable<BitmapImage> imageGridSource)
                    {
                        foreach (var gridImage in imageGridSource)
                        {
                            gridImage.UriSource = null;  // Clear image resources
                        }
                    }

                    ImageGridView.ItemsSource = null;  // Clear the image grid
                });

                // Clear the image list in memory
                imageFiles?.Clear();

                // Stop file watcher events
                if (fileWatcher != null)
                {
                    fileWatcher.EnableRaisingEvents = false;
                    fileWatcher.Path = string.Empty;
                }

                // Perform garbage collection to release memory
                GC.Collect();
                GC.WaitForPendingFinalizers();
            });
        }
        private void ReleaseCurrentResources()
        {
            if (SelectedImage.Source != null)
            {
                SelectedImage.Source = null;
            }

            // Trigger garbage collection only for current image
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // Hotkey Controls
        private void Grid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Check if Ctrl is pressed
            var isCtrlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);

            // Try to find the action in the dictionary
            if (keyMappings.TryGetValue((e.Key, isCtrlPressed), out var action))
            {
                action.Invoke(sender, e);  // Invoke the corresponding action
                e.Handled = true;          // Mark the event as handled
            }
        }
        // Misc Controls
        private void ReloadUI()
        {
            Frame rootFrame = this.Content as Frame;
            if (rootFrame != null && rootFrame.Content != null)
            {
                // Re-navigate to the current page to simulate a reload
                var currentPageType = rootFrame.Content.GetType();
                rootFrame.Navigate(currentPageType);
            }
        }
        private async Task ShowMessage(string message)
        {
            // Ensure dialog is only shown once at a time
            ContentDialog dialog = new ContentDialog
            {
                Title = "Notification",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot // Use the current XamlRoot in WinUI 3
            };

            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                // Catch and log any unexpected errors from the dialog system
                Debug.WriteLine($"Error showing dialog: {ex.Message}");
            }
        }
    }
}
