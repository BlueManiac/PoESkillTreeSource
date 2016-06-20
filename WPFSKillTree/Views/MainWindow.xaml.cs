﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MahApps.Metro;
using MahApps.Metro.Controls;
using MahApps.Metro.SimpleChildWindow;
using POESKillTree.Controls;
using POESKillTree.Controls.Dialogs;
using POESKillTree.Localization;
using POESKillTree.Model;
using POESKillTree.Model.Items;
using POESKillTree.SkillTreeFiles;
using POESKillTree.TreeGenerator.ViewModels;
using POESKillTree.TreeGenerator.Views;
using POESKillTree.Utils;
using POESKillTree.Utils.Converter;
using POESKillTree.Utils.Extensions;
using POESKillTree.ViewModels;
using Attribute = POESKillTree.ViewModels.Attribute;

namespace POESKillTree.Views
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow, INotifyPropertyChanged
    {
        /// <summary>
        /// The set of keys of which one needs to be pressed to highlight similar nodes on hover.
        /// </summary>
        private static readonly Key[] HighlightByHoverKeys = { Key.LeftShift, Key.RightShift };

        private readonly PersistentData _persistentData = App.PersistentData;

        public event PropertyChangedEventHandler PropertyChanged;

        public IPersistentData PersistentData
        {
            get { return _persistentData; }
        }

        private readonly List<ListGroupItem> _defenceList = new List<ListGroupItem>();
        private readonly Dictionary<string, AttributeGroup> _defenceListGroups = new Dictionary<string, AttributeGroup>();
        private readonly List<ListGroupItem> _offenceList = new List<ListGroupItem>();
        private readonly Dictionary<string, AttributeGroup> _offenceListGroups = new Dictionary<string, AttributeGroup>();


        private readonly ToolTip _sToolTip = new ToolTip();
        private readonly ToolTip _noteTip = new ToolTip();

        private ListCollectionView _defenceCollection;
        private ListCollectionView _offenceCollection;
        private RenderTargetBitmap _clipboardBmp;

        private async Task<SkillTree> CreateSkillTreeAsync(ProgressDialogController controller,
            AssetLoader assetLoader = null)
        {
            var tree = await SkillTree.CreateAsync(_persistentData, DialogCoordinator.Instance, controller, assetLoader);
            DialogParticipation.SetRegister(this, tree);
            tree.BanditSettings = _persistentData.CurrentBuild.Bandits;
            return tree;
        }

        private Vector2D _addtransform;
        private bool _justLoaded;
        private string _lasttooltip;

        private Vector2D _multransform;

        private List<ushort> _prePath;
        private HashSet<ushort> _toRemove;

        readonly Stack<string> _undoList = new Stack<string>();
        readonly Stack<string> _redoList = new Stack<string>();

        private Point _dragAndDropStartPoint;
        private DragAdorner _adorner;
        private AdornerLayer _layer;

        private MouseButton _lastMouseButton;
        /// <summary>
        /// The node of the SkillTree that currently has the mouse over it.
        /// Null if no node is under the mouse.
        /// </summary>
        private SkillNode _hoveredNode;

        private SkillNode _lastHoveredNode;

        private bool _noAsyncTaskRunning = true;
        /// <summary>
        /// Specifies if there is a task running asynchronously in the background.
        /// Used to disable UI buttons that might interfere with the result of the task.
        /// </summary>
        public bool NoAsyncTaskRunning
        {
            get { return _noAsyncTaskRunning; }
            private set
            {
                _noAsyncTaskRunning = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("NoAsyncTaskRunning"));
            }
        }

        private SettingsWindow _settingsWindow;

        private static readonly string MainWindowTitle =
            FileVersionInfo.GetVersionInfo(Assembly.GetEntryAssembly().Location).ProductName;

        public MainViewModel ViewModel => DataContext as MainViewModel;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = App.MainViewModel = new MainViewModel();

            // Register handlers
            PersistentData.CurrentBuild.PropertyChanged += CurrentBuildOnPropertyChanged;
            PersistentData.CurrentBuild.Bandits.PropertyChanged += (o, a) => UpdateUI();
            // Re-register handlers when PersistentData.CurrentBuild is set.
            PersistentData.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == "CurrentBuild")
                {
                    PersistentData.CurrentBuild.PropertyChanged += CurrentBuildOnPropertyChanged;
                    PersistentData.CurrentBuild.Bandits.PropertyChanged += (o, a) => UpdateUI();
                    if (ViewModel.Tree != null)
                        ViewModel.Tree.BanditSettings = PersistentData.CurrentBuild.Bandits;
                }
            };
            // This makes sure CurrentBuildOnPropertyChanged is called only
            // on the PoEBuild instance currently stored in PersistentData.CurrentBuild.
            PersistentData.PropertyChanging += (sender, args) =>
            {
                if (args.PropertyName == "CurrentBuild")
                {
                    PersistentData.CurrentBuild.PropertyChanged -= CurrentBuildOnPropertyChanged;
                }
            };
        }

        private async void CurrentBuildOnPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
        {
            if (propertyChangedEventArgs.PropertyName == "ItemData")
            {
                await LoadItemData();
            }
        }


        #region Window methods

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var controller = await this.ShowProgressAsync(L10n.Message("Initialization"),
                        L10n.Message("Initalizing window ..."));
            controller.Maximum = 1;
            controller.SetIndeterminate();

            await Task.Run(() =>
            {
                const string itemDBPrefix = "Data/ItemDB/";
                Directory.CreateDirectory(AppData.GetFolder(itemDBPrefix));
                // First file instantiates the ItemDB.
                ItemDB.Load(itemDBPrefix + "GemList.xml");
                // Merge all other files from the ItemDB path.
                Directory.GetFiles(AppData.GetFolder(itemDBPrefix))
                    .Select(Path.GetFileName)
                    .Where(f => f != "GemList.xml")
                    .Select(f => itemDBPrefix + f)
                    .ForEach(ItemDB.Merge);
                // Merge the user specified things.
                ItemDB.Merge("ItemsLocal.xml");
                ItemDB.Index();
            });



            _defenceCollection = new ListCollectionView(_defenceList);
            _defenceCollection.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
            listBoxDefence.ItemsSource = _defenceCollection;

            _offenceCollection = new ListCollectionView(_offenceList);
            _offenceCollection.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
            listBoxOffence.ItemsSource = _offenceCollection;

            ViewModel.CharacterClasses = CharacterNames.NameToContent.Select(x => x.Value).ToList();
            ViewModel.AscendancyClassIndex = 0;

            if (_persistentData.StashBookmarks != null)
                Stash.Bookmarks = new System.Collections.ObjectModel.ObservableCollection<StashBookmark>(_persistentData.StashBookmarks);

            // Set theme & accent.
            SetTheme(_persistentData.Options.Theme);
            SetAccent(_persistentData.Options.Accent);

            controller.SetMessage(L10n.Message("Loading skill tree assets ..."));
            ViewModel.Tree = await CreateSkillTreeAsync(controller);
            await Task.Delay(1); // Give the progress dialog a chance to update
            recSkillTree.Width = SkillTree.TRect.Width / SkillTree.TRect.Height * recSkillTree.Height;
            recSkillTree.UpdateLayout();
            recSkillTree.Fill = new VisualBrush(ViewModel.Tree.SkillTreeVisual);

            _multransform = SkillTree.TRect.Size / new Vector2D(recSkillTree.RenderSize.Width, recSkillTree.RenderSize.Height);
            _addtransform = SkillTree.TRect.TopLeft;

            controller.SetMessage(L10n.Message("Initalizing window ..."));
            controller.SetIndeterminate();
            await Task.Delay(1); // Give the progress dialog a chance to update

            _justLoaded = true;

            // loading last build
            if (_persistentData.CurrentBuild != null)
                await SetCurrentBuild(_persistentData.CurrentBuild);

            await LoadBuildFromUrlAsync();
            _justLoaded = false;
            // loading saved build
            lvSavedBuilds.Items.Clear();
            foreach (var build in _persistentData.Builds)
            {
                lvSavedBuilds.Items.Add(build);
            }
            _persistentData.Options.PropertyChanged += Options_PropertyChanged;
            PopulateAsendancySelectionList();
            await CheckAppVersionAndDoNecessaryChanges();

            await controller.CloseAsync();
        }

        private void Options_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == _persistentData.Options.Name(x => x.ShowAllAscendancyClasses))
                ViewModel.Tree.ToggleAscendancyTree(_persistentData.Options.ShowAllAscendancyClasses);
            SearchUpdate();
        }

        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.Q:
                        ToggleAttributes();
                        break;
                    case Key.B:
                        ToggleBuilds();
                        break;
                    case Key.R:
                        btnReset_Click(sender, e);
                        break;
                    case Key.E:
                        btnPoeUrl_Click(sender, e);
                        break;
                    case Key.D1:
                    case Key.D2:
                    case Key.D3:
                    case Key.D4:
                    case Key.D5:
                    case Key.D6:
                    case Key.D7:
                        ViewModel.UserInteraction = true;
                        var index = int.Parse(e.Key.ToString().Substring(1)) - 1;
                        ViewModel.CharacterClass = ViewModel.CharacterClasses[index];
                        ViewModel.AscendancyClassIndex = 0;
                        break;
                    case Key.Z:
                        tbSkillURL_Undo();
                        break;
                    case Key.Y:
                        tbSkillURL_Redo();
                        break;
                    case Key.S:
                        await SaveBuild();
                        break;
                    case Key.N:
                        await NewBuild();
                        break;
                }
            }

            if (HighlightByHoverKeys.Any(key => key == e.Key))
            {
                HighlightNodesByHover();
            }

            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                switch (e.Key)
                {
                    case Key.Q:
                        ToggleCharacterSheet();
                        break;
                }
            }

            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt))
            {
                switch (e.Key)
                {
                    case Key.S:
                        await SaveNewBuild();
                        break;
                }
            }
        }

        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (HighlightByHoverKeys.Any(key => key == e.Key))
            {
                HighlightNodesByHover();
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _persistentData.CurrentBuild.Url = ViewModel.SkillTreeUrl;
            _persistentData.CurrentBuild.Level = GetLevelAsString();
            _persistentData.SetBuilds(lvSavedBuilds.Items);
            _persistentData.StashBookmarks = Stash.Bookmarks.ToList();

            if (_settingsWindow != null)
            {
                _settingsWindow.Close();
            }
        }

        #endregion

        #region Utility
        private void SetTitle(string buildName)
        {
            Title = buildName + " - " + MainWindowTitle;
        }
        #endregion

        #region Menu

        private async void Menu_NewBuild(object sender, RoutedEventArgs e)
        {
            await NewBuild();
        }

        private async void Menu_SkillTaggedNodes(object sender, RoutedEventArgs e)
        {
            await ViewModel.Tree.SkillAllTaggedNodesAsync();
            UpdateUI();
            ViewModel.SkillTreeUrl = ViewModel.Tree.SaveToURL();
            ViewModel.Tree.LoadFromURL(ViewModel.SkillTreeUrl);
        }

        private async void Menu_UntagAllNodes(object sender, RoutedEventArgs e)
        {
            var response = await this.ShowQuestionAsync(L10n.Message("Are you sure?"),
                L10n.Message("Untag All Skill Nodes"), MessageBoxImage.None);
            if (response == MessageBoxResult.Yes)
                ViewModel.Tree.UntagAllNodes();
        }

        private void Menu_UnhighlightAllNodes(object sender, RoutedEventArgs e)
        {
            ViewModel.Tree.UnhighlightAllNodes();
            ClearSearch();
        }

        private void Menu_CheckAllHighlightedNodes(object sender, RoutedEventArgs e)
        {
            ViewModel.Tree.CheckAllHighlightedNodes();
        }

        private void Menu_CrossAllHighlightedNodes(object sender, RoutedEventArgs e)
        {
            ViewModel.Tree.CrossAllHighlightedNodes();
        }

        private async void Menu_OpenTreeGenerator(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_settingsWindow == null)
                {
                    var vm = new SettingsViewModel(ViewModel.Tree, SettingsDialogCoordinator.Instance);
                    vm.RunFinished += (o, args) =>
                    {
                        UpdateUI();
                        ViewModel.SkillTreeUrl = ViewModel.Tree.SaveToURL();
                        ViewModel.Tree.LoadFromURL(ViewModel.SkillTreeUrl);
                    };
                    _settingsWindow = new SettingsWindow { DataContext = vm};
                    DialogParticipation.SetRegister(_settingsWindow, vm);
                }
                await this.ShowChildWindowAsync(_settingsWindow);
            }
            catch (Exception ex)
            {
                await this.ShowErrorAsync(L10n.Message("Could not open Skill Tree Generator"), ex.Message);
            }
        }

        private async void Menu_ScreenShot(object sender, RoutedEventArgs e)
        {
            const int maxsize = 3000;
            Rect2D contentBounds = ViewModel.Tree.picActiveLinks.ContentBounds;
            contentBounds *= 1.2;
            if (!double.IsNaN(contentBounds.Width) && !double.IsNaN(contentBounds.Height))
            {
                double aspect = contentBounds.Width / contentBounds.Height;
                double xmax = contentBounds.Width;
                double ymax = contentBounds.Height;
                if (aspect > 1 && xmax > maxsize)
                {
                    xmax = maxsize;
                    ymax = xmax / aspect;
                }
                if (aspect < 1 & ymax > maxsize)
                {
                    ymax = maxsize;
                    xmax = ymax * aspect;
                }

                _clipboardBmp = new RenderTargetBitmap((int)xmax, (int)ymax, 96, 96, PixelFormats.Pbgra32);
                var db = new VisualBrush(ViewModel.Tree.SkillTreeVisual);
                db.ViewboxUnits = BrushMappingMode.Absolute;
                db.Viewbox = contentBounds;
                var dw = new DrawingVisual();

                using (DrawingContext dc = dw.RenderOpen())
                {
                    dc.DrawRectangle(db, null, new Rect(0, 0, xmax, ymax));
                }
                _clipboardBmp.Render(dw);
                _clipboardBmp.Freeze();

                //Save image in clipboard
                Clipboard.SetImage(_clipboardBmp);

                //Convert renderTargetBitmap to bitmap
                MemoryStream stream = new MemoryStream();
                BitmapEncoder encoder = new BmpBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(_clipboardBmp));
                encoder.Save(stream);

                System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(stream);
                System.Drawing.Image image = System.Drawing.Image.FromStream(stream);

                // Configure save file dialog box
                Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog();

                // Default file name -- current build name ("buildname - xxx points used")
                uint skilledNodes = (uint)ViewModel.Tree.GetPointCount()["NormalUsed"];
                dialog.FileName = PersistentData.CurrentBuild.Name + " - " + string.Format(L10n.Plural("{0} point", "{0} points", skilledNodes), skilledNodes);

                dialog.DefaultExt = ".jpg"; // Default file extension
                dialog.Filter = "JPEG (*.jpg, *.jpeg)|*.jpg;|PNG (*.png)|*.png"; // Filter files by extension
                dialog.OverwritePrompt = true;

                // Show save file dialog box
                bool? result = dialog.ShowDialog();

                // Continue if the user did select a path
                if (result.HasValue && result == true)
                {
                    System.Drawing.Imaging.ImageFormat format;
                    string fileExtension = System.IO.Path.GetExtension(dialog.FileName);

                    //set the selected data type
                    switch (fileExtension)
                    {
                        case ".png":
                            format = System.Drawing.Imaging.ImageFormat.Png;
                            break;

                        case ".jpg":
                        case ".jpeg":
                        default:
                            format = System.Drawing.Imaging.ImageFormat.Jpeg;
                            break;
                    }

                    //save the file
                    image.Save(dialog.FileName, format);
                }

                recSkillTree.Fill = new VisualBrush(ViewModel.Tree.SkillTreeVisual);
            }
            else
            {
                await this.ShowInfoAsync(L10n.Message("Your build must use at least one node to generate a screenshot"), title: "Screenshot Generator");
            }
        }

        private async void Menu_ImportItems(object sender, RoutedEventArgs e)
        {
            await this.ShowDialogAsync(
                new DownloadItemsViewModel(_persistentData.CurrentBuild),
                new DownloadItemsWindow());
        }

        private async void Menu_ImportStash(object sender, RoutedEventArgs e)
        {
            await this.ShowDialogAsync(
                new DownloadStashViewModel(DialogCoordinator.Instance, _persistentData, Stash),
                new DownloadStashWindow());
        }

        private async void Menu_CopyStats(object sender, RoutedEventArgs e)
        {
            var overview = AttributePanel.GetAttributesOverview();

            try
            {
                Clipboard.SetText(overview);
            }
            catch (Exception ex)
            {
                await this.ShowErrorAsync(L10n.Message("An error occurred while copying to Clipboard."), ex.Message);
            }
        }

        private async void Menu_RedownloadTreeAssets(object sender, RoutedEventArgs e)
        {
            string sMessageBoxText = L10n.Message("The existing Skill tree assets will be deleted and new assets will be downloaded.")
                                     + "\n\n" + L10n.Message("Do you want to continue?");

            var rsltMessageBox = await this.ShowQuestionAsync(sMessageBoxText, image: MessageBoxImage.Warning);
            switch (rsltMessageBox)
            {
                case MessageBoxResult.Yes:
                    var controller = await this.ShowProgressAsync(L10n.Message("Downloading skill tree assets ..."), null);
                    controller.Maximum = 1;
                    controller.SetProgress(0);
                    var assetLoader = new AssetLoader(new HttpClient(), AppData.GetFolder("Data", true), false);
                    try
                    {
                        assetLoader.MoveToBackup();

                        SkillTree.ClearAssets(); //enable recaching of assets
                        ViewModel.Tree = await CreateSkillTreeAsync(controller, assetLoader); //create new skilltree to reinitialize cache
                        recSkillTree.Fill = new VisualBrush(ViewModel.Tree.SkillTreeVisual);

                        await LoadBuildFromUrlAsync();
                        _justLoaded = false;

                        assetLoader.DeleteBackup();
                    }
                    catch (Exception ex)
                    {
                        assetLoader.RestoreBackup();
                        await this.ShowErrorAsync(L10n.Message("An error occurred while downloading assets."), ex.Message);
                    }
                    await controller.CloseAsync();
                    break;

                case MessageBoxResult.No:
                    //Do nothing
                    break;
            }
        }

        private void Menu_Exit(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Menu_OpenPoEWebsite(object sender, RoutedEventArgs e)
        {
            Process.Start("https://www.pathofexile.com/");
        }

        private void Menu_OpenWiki(object sender, RoutedEventArgs e)
        {
            Process.Start("http://pathofexile.gamepedia.com/");
        }

        private async void Menu_OpenHelp(object sender, RoutedEventArgs e)
        {
            await this.ShowDialogAsync(new CloseableViewModel(), new HelpWindow());
        }

        private async void Menu_OpenSettings(object sender, RoutedEventArgs e)
        {
            await this.ShowDialogAsync(
                new SettingsMenuViewModel(_persistentData, DialogCoordinator.Instance),
                new SettingsMenuWindow());
        }

        private async void Menu_OpenHotkeys(object sender, RoutedEventArgs e)
        {
            await this.ShowDialogAsync(new CloseableViewModel(), new HotkeysWindow());
        }

        private async void Menu_OpenAbout(object sender, RoutedEventArgs e)
        {
            await this.ShowDialogAsync(new CloseableViewModel(), new AboutWindow());
        }

        // Checks for updates.
        private async void Menu_CheckForUpdates(object sender, RoutedEventArgs e)
        {
            try
            {
                // No non-Task way without rewriting Updater to support/use await directly.
                var release =
                    await AwaitAsyncTask(L10n.Message("Checking for updates"),
                        Task.Run(() => Updater.CheckForUpdates()));

                if (release == null)
                {
                    await this.ShowInfoAsync(L10n.Message("You have the latest version!"));
                }
                else
                {
                    string message = release.IsUpdate
                        ? string.Format(L10n.Message("An update for {0} ({1}) is available!"),
                            Properties.Version.ProductName, release.Version)
                          + "\n\n" +
                          L10n.Message("The application will be closed when download completes to proceed with the update.")
                        : string.Format(L10n.Message("A new version {0} is available!"), release.Version)
                          + "\n\n" +
                          L10n.Message(
                              "The new version of application will be installed side-by-side with earlier versions.");

                    if (release.IsPrerelease)
                        message += "\n\n" +
                                   L10n.Message("Warning: This is a pre-release, meaning there could be some bugs!");

                    message += "\n\n" +
                               (release.IsUpdate
                                   ? L10n.Message("Do you want to download and install the update?")
                                   : L10n.Message("Do you want to download and install the new version?"));

                    var download = await this.ShowQuestionAsync(message, L10n.Message("Continue installation?"),
                        release.IsPrerelease ? MessageBoxImage.Warning : MessageBoxImage.Question);
                    if (download == MessageBoxResult.Yes)
                        await InstallUpdateAsync();
                    else
                        Updater.Dispose();
                }
            }
            catch (UpdaterException ex)
            {
                await this.ShowErrorAsync(
                    L10n.Message("An error occurred while attempting to contact the update location."),
                    ex.Message);
            }
        }

        // Starts update process.
        private async Task InstallUpdateAsync()
        {
            var controller = await this.ShowProgressAsync(L10n.Message("Downloading latest version"), null, true);
            controller.Maximum = 100;
            controller.Canceled += (sender, args) => Updater.Cancel();
            try
            {
                var downloadCs = new TaskCompletionSource<AsyncCompletedEventArgs>();
                Updater.Download((sender, args) => downloadCs.SetResult(args),
                    (sender, args) => controller.SetProgress(args.ProgressPercentage));

                var result = await downloadCs.Task;
                await controller.CloseAsync();
                await UpdateDownloadCompleted(result);
            }
            catch (UpdaterException ex)
            {
                await this.ShowErrorAsync(L10n.Message("An error occurred during the download operation."),
                    ex.Message);
                await controller.CloseAsync();
            }
        }

        // Invoked when update download completes, aborts or fails.
        private async Task UpdateDownloadCompleted(AsyncCompletedEventArgs e)
        {
            if (e.Cancelled) // Check whether download was cancelled.
            {
                Updater.Dispose();
            }
            else if (e.Error != null) // Check whether error occurred.
            {
                await this.ShowErrorAsync(L10n.Message("An error occurred during the download operation."), e.Error.Message);
            }
            else // Download completed.
            {
                try
                {
                    Updater.Install();
                    // Release being installed is an update, we have to exit application.
                    if (Updater.GetLatestRelease().IsUpdate) App.Current.Shutdown();
                }
                catch (UpdaterException ex)
                {
                    Updater.Dispose();
                    await this.ShowErrorAsync(L10n.Message("An error occurred while attempting to start the installation."), ex.Message);
                }
            }
        }

        #endregion

        #region  Character Selection
        private void userInteraction_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            ViewModel.UserInteraction = true;
        }

        private void cbCharType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
             if (ViewModel.Tree == null)
                return;
             if (!ViewModel.UserInteraction)
                 return;
             if (ViewModel.Tree.Chartype == ViewModel.CharacterClassIndex) return;

            ViewModel.Tree.Chartype = ViewModel.CharacterClassIndex;

            UpdateUI();
            ViewModel.SkillTreeUrl = ViewModel.Tree.SaveToURL();
            ViewModel.Tree.LoadFromURL(ViewModel.SkillTreeUrl);
            ViewModel.UserInteraction = false;
            PopulateAsendancySelectionList();
            ViewModel.AscendancyClassIndex = ViewModel.Tree.AscType = 0;
        }

        private void cbAscType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ViewModel.UserInteraction)
                return;
            if (ViewModel.AscendancyClassIndex < 0 || ViewModel.AscendancyClassIndex > 3)
                return;

            ViewModel.Tree.AscType = ViewModel.AscendancyClassIndex;

            UpdateUI();
            ViewModel.SkillTreeUrl = ViewModel.Tree.SaveToURL();
            ViewModel.Tree.LoadFromURL(ViewModel.SkillTreeUrl);
            ViewModel.UserInteraction = false;
        }

        private void PopulateAsendancySelectionList()
        {
            if (!ViewModel.Tree.UpdateAscendancyClasses) return;

            ViewModel.Tree.UpdateAscendancyClasses = false;

            ViewModel.AscendancyClasses = Enumerable
                .Repeat("None", 1)
                .Concat(ViewModel.Tree.AscendancyClasses.GetClasses(ViewModel.CharacterClass).Select(x => x.DisplayName))
                .ToList();
        }

        private string GetLevelAsString()
        {
            string level_string;
            try
            {
                level_string = ViewModel.Tree.Level.ToString(CultureInfo.CurrentCulture);
            }
            catch
            {
                level_string = "0";
            }
            return level_string;
        }

        private void SetLevelFromString(string s)
        {
            int lvl;
            if (int.TryParse(s, out lvl))
            {
                ViewModel.Tree.Level = lvl;
            }
        }

        private void Level_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> args)
        {
            UpdateUI();
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.Tree == null)
                return;
            ViewModel.Tree.Reset();
            UpdateUI();
            ViewModel.SkillTreeUrl = ViewModel.Tree.SaveToURL();
            ViewModel.Tree.LoadFromURL(ViewModel.SkillTreeUrl);
        }

        #endregion

        #region Update Attribute and Character lists

        public void UpdateUI()
        {
            AttributePanel.UpdateAttributeList();
            AttributePanel.UpdateAllAttributeList();
            AttributePanel.RefreshAttributeLists();
            UpdateStatistics();
            UpdateClass();
            UpdatePoints();
        }

        public void UpdateClass()
        {
            ViewModel.CharacterClassIndex = ViewModel.Tree.Chartype;
            ViewModel.AscendancyClassIndex = ViewModel.Tree.AscType;
        }

        public void UpdatePoints()
        {
            Dictionary<string, int> points = ViewModel.Tree.GetPointCount();
            NormalUsedPoints.Content = points["NormalUsed"].ToString();
            NormalTotalPoints.Content = points["NormalTotal"].ToString();
            AscendancyUsedPoints.Content = "[" + points["AscendancyUsed"] + "]";
            AscendancyTotalPoints.Content = "[" + points["AscendancyTotal"] + "]";
        }

        public void UpdateStatistics()
        {
            _defenceList.Clear();
            _offenceList.Clear();

            if (ViewModel.ItemAttributes != null)
            {
                Compute.Initialize(ViewModel.Tree, ViewModel.ItemAttributes);

                foreach (ListGroup group in Compute.Defense())
                {
                    foreach (var item in group.Properties.Select(AttributePanel.InsertNumbersInAttributes))
                    {
                        AttributeGroup attributeGroup;
                        if (!_defenceListGroups.TryGetValue(group.Name, out attributeGroup))
                        {
                            attributeGroup = new AttributeGroup(group.Name);
                            _defenceListGroups.Add(group.Name, attributeGroup);
                        }
                        _defenceList.Add(new ListGroupItem(item, attributeGroup));
                    }
                }

                foreach (ListGroup group in Compute.Offense())
                {
                    foreach (var item in group.Properties.Select(AttributePanel.InsertNumbersInAttributes))
                    {
                        AttributeGroup attributeGroup;
                        if (!_offenceListGroups.TryGetValue(group.Name, out attributeGroup))
                        {
                            attributeGroup = new AttributeGroup(group.Name);
                            _offenceListGroups.Add(group.Name, attributeGroup);
                        }
                        _offenceList.Add(new ListGroupItem(item, attributeGroup));
                    }
                }
            }

            _defenceCollection.Refresh();
            _offenceCollection.Refresh();
        }

        #endregion

        #region Attribute and Character lists - Event Handlers

        private void ToggleAttributes()
        {
            _persistentData.Options.AttributesBarOpened = !_persistentData.Options.AttributesBarOpened;
        }

        private void ToggleAttributes(bool expanded)
        {
            _persistentData.Options.AttributesBarOpened = expanded;
        }

        private void ToggleCharacterSheet()
        {
            _persistentData.Options.CharacterSheetBarOpened = !_persistentData.Options.CharacterSheetBarOpened;
        }

        private void ToggleCharacterSheet(bool expanded)
        {
            _persistentData.Options.CharacterSheetBarOpened = expanded;
        }

        private void expAttributes_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender == e.Source) // Ignore contained ListBox group collapsion events.
            {
                ToggleCharacterSheet(false);
            }
        }

        private void expAttributes_MouseLeave(object sender, MouseEventArgs e)
        {
            SearchUpdate();
        }

        private void expCharacterSheet_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender == e.Source) // Ignore contained ListBox group expansion events.
            {
                ToggleAttributes(false);
            }
        }

        private void ToggleBuilds()
        {
            _persistentData.Options.BuildsBarOpened = !_persistentData.Options.BuildsBarOpened;
        }

        #endregion

        #region zbSkillTreeBackground

        private void zbSkillTreeBackground_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _lastMouseButton = e.ChangedButton;
        }

        private void zbSkillTreeBackground_Click(object sender, RoutedEventArgs e)
        {
            Point p = ((MouseEventArgs)e.OriginalSource).GetPosition(zbSkillTreeBackground.Child);
            Size size = zbSkillTreeBackground.Child.DesiredSize;
            var v = new Vector2D(p.X, p.Y);

            v = v * _multransform + _addtransform;

            IEnumerable<KeyValuePair<ushort, SkillNode>> nodes =
                SkillTree.Skillnodes.Where(n => ((n.Value.Position - v).Length < 50)).ToList();
            if (ViewModel.Tree.drawAscendancy && ViewModel.Tree.AscType > 0)
            {
                var asn = SkillTree.Skillnodes[ViewModel.Tree.GetAscNodeId()];
                var bitmap = ViewModel.Tree.Assets["Classes" + asn.ascendancyName];

                nodes = SkillTree.Skillnodes.Where(n => (n.Value.ascendancyName != null || (Math.Pow(n.Value.Position.X - asn.Position.X, 2) + Math.Pow(n.Value.Position.Y - asn.Position.Y, 2)) > Math.Pow((bitmap.Height * 1.25 + bitmap.Width * 1.25) / 2, 2)) && ((n.Value.Position - v).Length < 50)).ToList();
            }
            var className = CharacterNames.GetClassNameFromChartype(ViewModel.Tree.Chartype);
            SkillNode node = null;
            if (nodes.Count() != 0 && !ViewModel.Tree.drawAscendancy)
                node = nodes.First().Value;
            else if (nodes.Count() != 0)
            {
                var dnode = nodes.First();
                node = nodes.Where(x => x.Value.ascendancyName == ViewModel.Tree.AscendancyClasses.GetClassName(className, ViewModel.Tree.AscType)).DefaultIfEmpty(dnode).First().Value;
            }

            if (node != null && !SkillTree.rootNodeList.Contains(node.Id))
            {
                if (node.ascendancyName != null && !ViewModel.Tree.drawAscendancy)
                    return;
                var ascendancyClassName = ViewModel.Tree.AscendancyClasses.GetClassName(className, ViewModel.Tree.AscType);
                if (!_persistentData.Options.ShowAllAscendancyClasses && node.ascendancyName != null && node.ascendancyName != ascendancyClassName)
                    return;
                // Ignore clicks on character portraits and masteries
                if (node.Spc == null && node.Type != NodeType.Mastery)
                {
                    if (_lastMouseButton == MouseButton.Right)
                    {
                        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        {
                            // Backward on shift+RMB
                            ViewModel.Tree.CycleNodeTagBackward(node);
                        }
                        else
                        {
                            // Forward on RMB
                            ViewModel.Tree.CycleNodeTagForward(node);
                        }
                        e.Handled = true;
                    }
                    else
                    {
                        // Toggle whether the node is included in the tree
                        if (ViewModel.Tree.SkilledNodes.Contains(node.Id))
                        {
                            ViewModel.Tree.ForceRefundNode(node.Id);
                            _prePath = ViewModel.Tree.GetShortestPathTo(node.Id, ViewModel.Tree.SkilledNodes);
                            ViewModel.Tree.DrawPath(_prePath);
                        }
                        else if (_prePath != null)
                        {
                            foreach (ushort i in _prePath)
                            {
                                var temp = SkillTree.Skillnodes[i];
                                if (temp.IsMultipleChoiceOption)
                                {
                                    //Emmitt 20160401: This is for Scion Ascendancy MultipleChoice nodes
                                    foreach(var j in ViewModel.Tree.SkilledNodes)
                                    {
                                        if (SkillTree.Skillnodes[j].IsMultipleChoiceOption && ViewModel.Tree.AscendancyClasses.GetStartingClass(SkillTree.Skillnodes[i].Name) == ViewModel.Tree.AscendancyClasses.GetStartingClass(SkillTree.Skillnodes[j].Name))
                                        {
                                            ViewModel.Tree.SkilledNodes.Remove(j);
                                            break;
                                        }
                                    }
                                }
                                else if (temp.IsAscendancyStart)
                                {
                                    var remove = ViewModel.Tree.SkilledNodes.Where(x => SkillTree.Skillnodes[x].ascendancyName != null && SkillTree.Skillnodes[x].ascendancyName != temp.ascendancyName).ToArray();
                                    foreach (var n in remove)
                                        ViewModel.Tree.SkilledNodes.Remove(n);
                                }
                                ViewModel.Tree.SkilledNodes.Add(i);
                            }

                            _toRemove = ViewModel.Tree.ForceRefundNodePreview(node.Id);
                            if (_toRemove != null)
                                ViewModel.Tree.DrawRefundPreview(_toRemove);
                        }
                    }
                }
                ViewModel.SkillTreeUrl = ViewModel.Tree.SaveToURL();
                ViewModel.Tree.LoadFromURL(ViewModel.SkillTreeUrl);
                UpdateUI();
            }
            else if ((ViewModel.Tree.ascedancyButtonPos - v).Length < 150)
            {
                ViewModel.Tree.DrawAscendancyButton("Pressed");
                ViewModel.Tree.ToggleAscendancyTree();
                SearchUpdate();
            }
            else
            {
                if (p.X < 0 || p.Y < 0 || p.X > size.Width || p.Y > size.Height)
                {
                    if (_lastMouseButton == MouseButton.Right)
                    {
                        zbSkillTreeBackground.Reset();
                    }
                }
            }
        }

        private void zbSkillTreeBackground_MouseLeave(object sender, MouseEventArgs e)
        {
            // We might have popped up a tooltip while the window didn't have focus,
            // so we should close tooltips whenever the mouse leaves the canvas in addition to
            // whenever we lose focus.
            _sToolTip.IsOpen = false;
        }

        private void zbSkillTreeBackground_MouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(zbSkillTreeBackground.Child);
            var v = new Vector2D(p.X, p.Y);
            v = v * _multransform + _addtransform;

            IEnumerable<KeyValuePair<ushort, SkillNode>> nodes =
                SkillTree.Skillnodes.Where(n => ((n.Value.Position - v).Length < 50)).ToList();
            if(ViewModel.Tree.drawAscendancy && ViewModel.Tree.AscType > 0)
            {
                var asn = SkillTree.Skillnodes[ViewModel.Tree.GetAscNodeId()];
                var bitmap = ViewModel.Tree.Assets["Classes" + asn.ascendancyName];

                nodes = SkillTree.Skillnodes.Where(n => (n.Value.ascendancyName != null || (Math.Pow(n.Value.Position.X - asn.Position.X, 2) + Math.Pow(n.Value.Position.Y - asn.Position.Y, 2)) > Math.Pow((bitmap.Height * 1.25 + bitmap.Width * 1.25) / 2, 2)) && ((n.Value.Position - v).Length < 50)).ToList();
            }
            var className = CharacterNames.GetClassNameFromChartype(ViewModel.Tree.Chartype);
            SkillNode node = null;
            if (nodes.Count() != 0 && !ViewModel.Tree.drawAscendancy)
                node = nodes.First().Value;
            else if (nodes.Count() != 0)
            {
                var dnode = nodes.First();
                node = nodes.Where(x => x.Value.ascendancyName == ViewModel.Tree.AscendancyClasses.GetClassName(className, ViewModel.Tree.AscType)).DefaultIfEmpty(dnode).First().Value;
            }


            _hoveredNode = node;
            if (node != null && !SkillTree.rootNodeList.Contains(node.Id))
            {
                if (!ViewModel.Tree.drawAscendancy && node.ascendancyName != null)
                    return;
                if (!_persistentData.Options.ShowAllAscendancyClasses && node.ascendancyName != null && node.ascendancyName != ViewModel.Tree.AscendancyClasses.GetClassName(className, ViewModel.Tree.AscType))
                    return;
                if (node.Type == NodeType.JewelSocket)
                {
                    ViewModel.Tree.DrawJewelHighlight(node);
                }

                if (ViewModel.Tree.SkilledNodes.Contains(node.Id))
                {
                    _toRemove = ViewModel.Tree.ForceRefundNodePreview(node.Id);
                    if (_toRemove != null)
                        ViewModel.Tree.DrawRefundPreview(_toRemove);
                }
                else
                {
                    _prePath = ViewModel.Tree.GetShortestPathTo(node.Id, ViewModel.Tree.SkilledNodes);
                    if (node.Type != NodeType.Mastery)
                        ViewModel.Tree.DrawPath(_prePath);
                }
                var tooltip = node.Name;
                if (node.Attributes.Count != 0)
                    tooltip += "\n" + node.attributes.Aggregate((s1, s2) => s1 + "\n" + s2);
                if (!(_sToolTip.IsOpen && _lasttooltip == tooltip))
                {
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock
                    {
                        Text = tooltip
                    });
                    if(node.reminderText != null)
                    {
                        sp.Children.Add(new Separator());
                        sp.Children.Add(new TextBlock { Text = node.reminderText.Aggregate((s1, s2) => s1 + '\n' + s2) });
                    }
                    if (_prePath != null && node.Type != NodeType.Mastery)
                    {
                        var points = _prePath.Count;
                        if(_prePath.Any(x => SkillTree.Skillnodes[x].IsAscendancyStart))
                            points--;
                        sp.Children.Add(new Separator());
                        sp.Children.Add(new TextBlock { Text = "Points to skill node: " + points });
                    }

                    _sToolTip.Content = sp;
                    if (!HighlightByHoverKeys.Any(Keyboard.IsKeyDown))
                    {
                        _sToolTip.IsOpen = true;
                    }
                    _lasttooltip = tooltip;
                }
            }
            else if ((ViewModel.Tree.ascedancyButtonPos - v).Length < 150)
            {
                ViewModel.Tree.DrawAscendancyButton("Highlight");
            }
            else
            {
                _sToolTip.Tag = false;
                _sToolTip.IsOpen = false;
                _prePath = null;
                _toRemove = null;
                if (ViewModel.Tree != null)
                {
                    ViewModel.Tree.ClearPath();
                    ViewModel.Tree.ClearJewelHighlight();
                    ViewModel.Tree.DrawAscendancyButton();
                }
            }
        }

        private void zbSkillTreeBackground_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            //zbSkillTreeBackground.Child.RaiseEvent(e);
        }

        private void HighlightNodesByHover()
        {
            if (ViewModel.Tree == null)
            {
                return;
            }

            if (_hoveredNode == null || _hoveredNode.Attributes.Count == 0 ||
                !HighlightByHoverKeys.Any(Keyboard.IsKeyDown))
            {
                if (_hoveredNode != null && _hoveredNode.Attributes.Count > 0)
                {
                    _sToolTip.IsOpen = true;
                }

                ViewModel.Tree.HighlightNodesBySearch("", true, NodeHighlighter.HighlightState.FromHover);

                _lastHoveredNode = null;
            }
            else
            {
                _sToolTip.IsOpen = false;

                if (_lastHoveredNode == _hoveredNode)
                {
                    // Not necessary, but stops it from continuously searching when holding down shift.
                    return;
                }

                var search = _hoveredNode.Attributes.Aggregate("^(", (current, attr) => current + (attr.Key + "|"));
                search = search.Substring(0, search.Length - 1);
                search += ")$";
                search = Regex.Replace(search, @"(\+|\-|\%)", @"\$1");
                search = Regex.Replace(search, @"\#", @"[0-9]*\.?[0-9]+");

                ViewModel.Tree.HighlightNodesBySearch(search, true, NodeHighlighter.HighlightState.FromHover,
                    _hoveredNode.Attributes.Count); // Remove last parameter to highlight nodes with any of the attributes.

                _lastHoveredNode = _hoveredNode;
            }
        }

        #endregion

        #region Items

        private bool _pauseLoadItemData;

        private async Task LoadItemData()
        {
            if (_pauseLoadItemData)
                return;

            if (ViewModel.ItemAttributes != null)
            {
                ViewModel.ItemAttributes.Equip.CollectionChanged -= ItemAttributesEquipCollectionChanged;
                ViewModel.ItemAttributes.PropertyChanged -= ItemAttributesPropertyChanged;
            }

            var itemData = _persistentData.CurrentBuild.ItemData;
            ItemAttributes itemAttributes;
            if (!string.IsNullOrEmpty(itemData))
            {
                try
                {
                    itemAttributes = new ItemAttributes(_persistentData, itemData);
                }
                catch (Exception ex)
                {
                    itemAttributes = new ItemAttributes();
                    await this.ShowErrorAsync(L10n.Message("An error occurred while attempting to load item data."),
                        ex.Message);
                }
            }
            else
            {
                itemAttributes = new ItemAttributes();
            }

            itemAttributes.Equip.CollectionChanged += ItemAttributesEquipCollectionChanged;
            itemAttributes.PropertyChanged += ItemAttributesPropertyChanged;
            ViewModel.ItemAttributes = itemAttributes;
            UpdateUI();
        }

        private void ItemAttributesPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            UpdateUI();
        }

        private void ItemAttributesEquipCollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            _pauseLoadItemData = true;
            _persistentData.CurrentBuild.ItemData = ViewModel.ItemAttributes.ToJsonString();
            _pauseLoadItemData = false;
        }

        #endregion

        #region Builds - Event Handlers

        private void SavedBuildFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SavedBuildFilterChanged();
        }

        private void SavedBuildFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            SavedBuildFilterChanged();
        }

        private void SavedBuildFilterChanged()
        {
            if (lvSavedBuilds == null) return;

            var selectedItem = (ComboBoxItem)cbCharTypeSavedBuildFilter.SelectedItem;
            var className = selectedItem.Content.ToString();
            var filterText = tbSavedBuildFilter.Text;

            foreach (PoEBuild item in lvSavedBuilds.Items)
            {
                item.Visible = (className.Equals("All", StringComparison.InvariantCultureIgnoreCase) ||
                                item.Class.Equals(className, StringComparison.InvariantCultureIgnoreCase)) &&
                               (item.Name.Contains(filterText, StringComparison.InvariantCultureIgnoreCase) ||
                                item.Note.Contains(filterText, StringComparison.InvariantCultureIgnoreCase));
            }

            lvSavedBuilds.Items.Refresh();
        }

        private async void lvi_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var lvi = ((ListView)sender).SelectedItem;
            if (lvi == null) return;
            var build = ((PoEBuild)lvi);
            await SetCurrentBuild(build);
            await LoadBuildFromUrlAsync(); // loading the build
        }

        private void lvi_MouseLeave(object sender, MouseEventArgs e)
        {
            _noteTip.IsOpen = false;
        }

        private void lvi_MouseEnter(object sender, MouseEventArgs e)
        {
            var highlightedItem = FindListViewItem(e);
            if (highlightedItem != null)
            {
                var build = (PoEBuild)highlightedItem.Content;
                _noteTip.Content = build.Note == @"" ? L10n.Message("Right click to edit") : build.Note;
                _noteTip.IsOpen = true;
            }
        }

        private async void lvi_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var selectedBuild = (PoEBuild)lvSavedBuilds.SelectedItem;
            var vm = new EditBuildViewModel(selectedBuild);
            if (!await this.ShowDialogAsync(vm, new EditBuildWindow()))
                return;

            selectedBuild.Name = vm.Build.Name;
            selectedBuild.Note = vm.Build.Note;
            selectedBuild.CharacterName = vm.Build.CharacterName;
            selectedBuild.AccountName = vm.Build.AccountName;
            lvSavedBuilds.Items.Refresh();
            if(selectedBuild.CurrentlyOpen)
                SetTitle(selectedBuild.Name);

            await SaveBuildsToFile();
        }

        private ListViewItem FindListViewItem(MouseEventArgs e)
        {
            var visualHitTest = VisualTreeHelper.HitTest(lvSavedBuilds, e.GetPosition(lvSavedBuilds)).VisualHit;

            ListViewItem listViewItem = null;

            while (visualHitTest != null)
            {
                if (visualHitTest is ListViewItem)
                {
                    listViewItem = visualHitTest as ListViewItem;

                    break;
                }
                if (Equals(visualHitTest, lvSavedBuilds))
                {
                    return null;
                }

                visualHitTest = VisualTreeHelper.GetParent(visualHitTest);
            }

            return listViewItem;
        }

        private async void btnSaveBuild_Click(object sender, RoutedEventArgs e)
        {
            await SaveBuild();
        }

        private async void btnSaveNewBuild_Click(object sender, RoutedEventArgs e)
        {
            await SaveNewBuild();
        }

        private async void btnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (lvSavedBuilds.SelectedItems.Count <= 0) return;

            if(((PoEBuild)lvSavedBuilds.SelectedItem).CurrentlyOpen)
                await NewBuild();
            lvSavedBuilds.Items.Remove(lvSavedBuilds.SelectedItem);
            await SaveBuildsToFile();
        }

        private async void lvSavedBuilds_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) &&
                lvSavedBuilds.SelectedIndex > 0)
            {
                await MoveBuildInList(-1);
            }

            else if (e.Key == Key.Down && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) &&
                     lvSavedBuilds.SelectedIndex < lvSavedBuilds.Items.Count - 1)
            {
                await MoveBuildInList(1);
            }
        }

        private async Task MoveBuildInList(int direction)
        {
            var obj = lvSavedBuilds.Items[lvSavedBuilds.SelectedIndex];
            var selectedIndex = lvSavedBuilds.SelectedIndex;
            lvSavedBuilds.Items.RemoveAt(selectedIndex);
            lvSavedBuilds.Items.Insert(selectedIndex + direction, obj);
            lvSavedBuilds.SelectedItem = lvSavedBuilds.Items[selectedIndex + direction];
            lvSavedBuilds.SelectedIndex = selectedIndex + direction;
            lvSavedBuilds.Items.Refresh();

            await SaveBuildsToFile();
        }

        #endregion

        #region Builds - Services
        private async Task SetCurrentBuild(PoEBuild build)
        {
            foreach (PoEBuild item in lvSavedBuilds.Items)
            {
                item.CurrentlyOpen = false;
            }
            build.CurrentlyOpen = true;
            lvSavedBuilds.Items.Refresh();
            SetTitle(build.Name);

            _persistentData.CurrentBuild = PoEBuild.Copy(build);

            ViewModel.SkillTreeUrl = build.Url;
            SetLevelFromString(build.Level);
            await LoadItemData();
            AttributePanel.SetCustomGroups(build.CustomGroups);
        }

        private async Task NewBuild()
        {
            await SetCurrentBuild(new PoEBuild
            {
                Name = "New Build",
                Url = Constants.TreeAddress + SkillTree.GetCharacterURL(3),
                Level = "1"
            });
            await LoadBuildFromUrlAsync();
        }

        private async Task SaveBuild()
        {
            var currentOpenBuild =
                (from PoEBuild build in lvSavedBuilds.Items
                 where build.CurrentlyOpen
                 select build).FirstOrDefault();
            if (currentOpenBuild != null)
            {
                currentOpenBuild.Class = ViewModel.CharacterClass;
                currentOpenBuild.CharacterName = _persistentData.CurrentBuild.CharacterName;
                currentOpenBuild.AccountName = _persistentData.CurrentBuild.AccountName;
                currentOpenBuild.Level = GetLevelAsString();
                currentOpenBuild.PointsUsed = NormalUsedPoints.Content.ToString();
                currentOpenBuild.Url = ViewModel.SkillTreeUrl;
                currentOpenBuild.ItemData = _persistentData.CurrentBuild.ItemData;
                currentOpenBuild.LastUpdated = DateTime.Now;
                currentOpenBuild.CustomGroups = AttributePanel.ViewModel.AttributeGroups.CopyCustomGroups();
                currentOpenBuild.Bandits = _persistentData.CurrentBuild.Bandits.Clone();
                currentOpenBuild.League = _persistentData.CurrentBuild.League;
                await SetCurrentBuild(currentOpenBuild);
                await SaveBuildsToFile();
            }
            else
            {
                await SaveNewBuild();
            }
        }

        private async Task SaveNewBuild()
        {
            var vm = new EditBuildViewModel(_persistentData.CurrentBuild);
            if (!await this.ShowDialogAsync(vm, new EditBuildWindow()))
                return;

            var newBuild = vm.Build;
            newBuild.LastUpdated = DateTime.Now;
            newBuild.Level = GetLevelAsString();
            newBuild.Class = ViewModel.CharacterClass;
            newBuild.PointsUsed = NormalUsedPoints.Content.ToString();
            newBuild.Url = ViewModel.SkillTreeUrl;
            newBuild.CustomGroups = AttributePanel.ViewModel.AttributeGroups.CopyCustomGroups();

            await SetCurrentBuild(newBuild);
            lvSavedBuilds.Items.Add(newBuild);

            if (lvSavedBuilds.Items.Count > 0)
            {
                await SaveBuildsToFile();
            }
            lvSavedBuilds.SelectedIndex = lvSavedBuilds.Items.Count - 1;
            if (lvSavedBuilds.SelectedIndex != -1)
                lvSavedBuilds.ScrollIntoView(lvSavedBuilds.Items[lvSavedBuilds.Items.Count - 1]);
        }

        private async Task SaveBuildsToFile()
        {
            try
            {
                _persistentData.SetBuilds(lvSavedBuilds.Items);
                _persistentData.SaveToFile();
            }
            catch (Exception e)
            {
                await this.ShowErrorAsync(L10n.Message("An error occurred during a save operation."), e.Message);
            }
        }

        private async Task LoadBuildFromUrlAsync()
        {
            try
            {
                ViewModel.UserInteraction = true;
                if (ViewModel.SkillTreeUrl.Contains("poezone.ru"))
                {
                    await SkillTreeImporter.LoadBuildFromPoezone(DialogCoordinator.Instance, ViewModel.Tree, ViewModel.SkillTreeUrl);
                    ViewModel.SkillTreeUrl = ViewModel.Tree.SaveToURL();
                }
                else if (ViewModel.SkillTreeUrl.Contains("google.com"))
                {
                    Match match = Regex.Match(ViewModel.SkillTreeUrl, @"q=(.*?)&");
                    if (match.Success)
                    {
                        ViewModel.SkillTreeUrl = match.ToString().Replace("q=", "").Replace("&", "");
                        await LoadBuildFromUrlAsync();
                    }
                    else
                        throw new Exception("The URL you are trying to load is invalid.");
                }
                else if (ViewModel.SkillTreeUrl.Contains("tinyurl.com") || ViewModel.SkillTreeUrl.Contains("poeurl.com"))
                {
                    var skillUrl = ViewModel.SkillTreeUrl.Replace("preview.", "");
                    if (skillUrl.Contains("poeurl.com") && !skillUrl.Contains("redirect.php"))
                    {
                        skillUrl = skillUrl.Replace("http://poeurl.com/",
                            "http://poeurl.com/redirect.php?url=");
                    }

                    var response =
                        await AwaitAsyncTask(L10n.Message("Resolving shortened tree address"),
                            new HttpClient().GetAsync(skillUrl, HttpCompletionOption.ResponseHeadersRead));
                    response.EnsureSuccessStatusCode();
                    if (Regex.IsMatch(response.RequestMessage.RequestUri.ToString(), Constants.TreeRegex))
                        ViewModel.SkillTreeUrl = response.RequestMessage.RequestUri.ToString();
                    else
                        throw new Exception("The URL you are trying to load is invalid.");
                    await LoadBuildFromUrlAsync();
                }
                else
                {
                    if (ViewModel.SkillTreeUrl.Contains("characterName") || ViewModel.SkillTreeUrl.Contains("accountName"))
                        ViewModel.SkillTreeUrl = Regex.Replace(ViewModel.SkillTreeUrl, @"\?.*", "");
                    ViewModel.SkillTreeUrl = Regex.Replace(ViewModel.SkillTreeUrl, Constants.TreeRegex, Constants.TreeAddress);
                    ViewModel.Tree.LoadFromURL(ViewModel.SkillTreeUrl);
                }

                if (_justLoaded)
                {
                    if (_undoList.Count > 1)
                    {
                        string holder = _undoList.Pop();
                        while (_undoList.Count != 0)
                            _undoList.Pop();
                        _undoList.Push(holder);
                    }
                }
                else
                {
                    UpdateClass();
                    ViewModel.Tree.UpdateAscendancyClasses = true;
                    PopulateAsendancySelectionList();
                }
                UpdateUI();
                _justLoaded = false;
            }
            catch (Exception ex)
            {
                ViewModel.SkillTreeUrl = ViewModel.Tree.SaveToURL();
                ViewModel.Tree.LoadFromURL(ViewModel.SkillTreeUrl);
                await this.ShowErrorAsync(L10n.Message("An error occurred while attempting to load Skill tree from URL."), ex.Message);
            }
        }

        #endregion

        #region Builds - DragAndDrop

        private void ListViewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragAndDropStartPoint = e.GetPosition(lvSavedBuilds);
        }

        private async void ListViewPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point position = e.GetPosition(lvSavedBuilds);

                if (Math.Abs(position.X - _dragAndDropStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragAndDropStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    await BeginDrag(e);
                }
            }
        }

        private async Task BeginDrag(MouseEventArgs e)
        {
            var listView = lvSavedBuilds;
            var listViewItem = ((DependencyObject)e.OriginalSource).TryFindParent<ListViewItem>();

            if (listViewItem == null)
                return;

            // get the data for the ListViewItem
            var item = listView.ItemContainerGenerator.ItemFromContainer(listViewItem);

            //setup the drag adorner.
            InitialiseAdorner(listViewItem);

            //add handles to update the adorner.
            listView.PreviewDragOver += ListViewDragOver;
            listView.DragLeave += ListViewDragLeave;
            listView.DragEnter += ListViewDragEnter;

            var data = new DataObject("myFormat", item);
            DragDrop.DoDragDrop(lvSavedBuilds, data, DragDropEffects.Move);

            //cleanup
            listView.PreviewDragOver -= ListViewDragOver;
            listView.DragLeave -= ListViewDragLeave;
            listView.DragEnter -= ListViewDragEnter;

            if (_adorner != null)
            {
                AdornerLayer.GetAdornerLayer(listView).Remove(_adorner);
                _adorner = null;
                await SaveBuildsToFile();
            }
        }

        private void ListViewDragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("myFormat") ||
                sender == e.Source)
            {
                e.Effects = DragDropEffects.None;
            }
        }


        private void ListViewDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("myFormat")) return;

            var name = e.Data.GetData("myFormat");
            var listView = lvSavedBuilds;
            var listViewItem = ((DependencyObject)e.OriginalSource).TryFindParent<ListViewItem>();

            if (listViewItem != null)
            {
                var itemToReplace = listView.ItemContainerGenerator.ItemFromContainer(listViewItem);
                int index = listView.Items.IndexOf(itemToReplace);

                if (index >= 0)
                {
                    listView.Items.Remove(name);
                    listView.Items.Insert(index, name);
                }
            }
            else
            {
                listView.Items.Remove(name);
                listView.Items.Add(name);
            }
        }

        private void InitialiseAdorner(UIElement listViewItem)
        {
            var brush = new VisualBrush(listViewItem);
            _adorner = new DragAdorner(listViewItem, listViewItem.RenderSize, brush) { Opacity = 0.5 };
            _layer = AdornerLayer.GetAdornerLayer(lvSavedBuilds);
            _layer.Add(_adorner);
        }

        void ListViewDragLeave(object sender, DragEventArgs e)
        {
            if (Equals(e.OriginalSource, lvSavedBuilds))
            {
                var p = e.GetPosition(lvSavedBuilds);
                var r = VisualTreeHelper.GetContentBounds(lvSavedBuilds);
                if (!r.Contains(p))
                {
                    e.Handled = true;
                }
            }
        }

        void ListViewDragOver(object sender, DragEventArgs args)
        {
            if (_adorner != null)
            {
                _adorner.OffsetLeft = args.GetPosition(lvSavedBuilds).X - _dragAndDropStartPoint.X;
                _adorner.OffsetTop = args.GetPosition(lvSavedBuilds).Y - _dragAndDropStartPoint.Y;
            }
        }

        #endregion

        #region Bottom Bar (Build URL etc)

        private void tbSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchUpdate();
        }

        private void cbRegEx_Click(object sender, RoutedEventArgs e)
        {
            SearchUpdate();
        }

        private void SearchUpdate()
        {
            ViewModel.Tree.HighlightNodesBySearch(tbSearch.Text, cbRegEx.IsChecked != null && cbRegEx.IsChecked.Value, NodeHighlighter.HighlightState.FromSearch);
        }

        private void ClearSearch()
        {
            tbSearch.Text = "";
            SearchUpdate();
        }

        private async void tbSkillURL_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && NoAsyncTaskRunning)
                await LoadBuildFromUrlAsync();
        }

        private void tbSkillURL_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SkillTreeUrlTextBox.SelectAll();
        }

        private void tbSkillURL_TextChanged(object sender, TextChangedEventArgs e)
        {
            _undoList.Push(ViewModel.SkillTreeUrl);
        }

        private void tbSkillURL_Undo_Click(object sender, RoutedEventArgs e)
        {
            tbSkillURL_Undo();
        }

        private void tbSkillURL_Undo()
        {
            if (_undoList.Count <= 0) return;
            if (_undoList.Peek() == ViewModel.SkillTreeUrl && _undoList.Count > 1)
            {
                _undoList.Pop();
                tbSkillURL_Undo();
            }
            else if (_undoList.Peek() != ViewModel.SkillTreeUrl)
            {
                _redoList.Push(ViewModel.SkillTreeUrl);
                ViewModel.SkillTreeUrl = _undoList.Pop();
                ViewModel.Tree.LoadFromURL(ViewModel.SkillTreeUrl);
                UpdateUI();
            }
        }

        private void tbSkillURL_Redo_Click(object sender, RoutedEventArgs e)
        {
            tbSkillURL_Redo();
        }

        private void tbSkillURL_Redo()
        {
            if (_redoList.Count <= 0) return;
            if (_redoList.Peek() == ViewModel.SkillTreeUrl && _redoList.Count > 1)
            {
                _redoList.Pop();
                tbSkillURL_Redo();
            }
            else if (_redoList.Peek() != ViewModel.SkillTreeUrl)
            {
                ViewModel.SkillTreeUrl = _redoList.Pop();
                ViewModel.Tree.LoadFromURL(ViewModel.SkillTreeUrl);
                UpdateUI();
            }
        }

        private async void btnLoadBuild_Click(object sender, RoutedEventArgs e)
        {
            await LoadBuildFromUrlAsync();
        }

        private async void btnPoeUrl_Click(object sender, RoutedEventArgs e)
        {
            await DownloadPoeUrlAsync();
        }

        private async Task DownloadPoeUrlAsync()
        {
            var regx =
                new Regex(
                    "https?://([\\w+?\\.\\w+])+([a-zA-Z0-9\\~\\!\\@\\#\\$\\%\\^\\&amp;\\*\\(\\)_\\-\\=\\+\\\\\\/\\?\\.\\:\\;\\'\\,]*)?",
                    RegexOptions.IgnoreCase);

            var matches = regx.Matches(ViewModel.SkillTreeUrl);

            if (matches.Count == 1)
            {
                try
                {
                    var url = matches[0].ToString();
                    if (!url.ToLower().StartsWith(Constants.TreeAddress))
                    {
                        return;
                    }
                    // PoEUrl can't handle https atm.
                    url = url.Replace("https://", "http://");

                    var result =
                        await AwaitAsyncTask(L10n.Message("Generating PoEUrl of Skill tree"),
                            new HttpClient().GetStringAsync("http://poeurl.com/shrink.php?url=" + url));
                    await ShowPoeUrlMessageAndAddToClipboard("http://poeurl.com/" + result.Trim());
                }
                catch (Exception ex)
                {
                    await this.ShowErrorAsync(L10n.Message("An error occurred while attempting to contact the PoEUrl location."), ex.Message);
                }
            }
        }

        private async Task ShowPoeUrlMessageAndAddToClipboard(string poeurl)
        {
            try
            {
                System.Windows.Forms.Clipboard.SetDataObject(poeurl, true);
                await this.ShowInfoAsync(L10n.Message("The PoEUrl link has been copied to Clipboard.") + "\n\n" + poeurl);
            }
            catch (Exception ex)
            {
                await this.ShowErrorAsync(L10n.Message("An error occurred while copying to Clipboard."), ex.Message);
            }
        }

        #endregion

        #region Theme

        private void mnuSetTheme_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            if (menuItem == null) return;

            SetTheme(menuItem.Tag as string);
        }

        private void SetTheme(string sTheme)
        {
            var accent = ThemeManager.Accents.First(x => Equals(x.Name, _persistentData.Options.Accent));
            var theme = ThemeManager.GetAppTheme("Base" + sTheme);
            ThemeManager.ChangeAppStyle(Application.Current, accent, theme);
            ((MenuItem)NameScope.GetNameScope(this).FindName("mnuViewTheme" + sTheme)).IsChecked = true;
            _persistentData.Options.Theme = sTheme;
        }

        private void mnuSetAccent_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            if (menuItem == null) return;

            SetAccent(menuItem.Tag as string);
        }

        private void SetAccent(string sAccent)
        {
            var accent = ThemeManager.Accents.First(x => Equals(x.Name, sAccent));
            var theme = ThemeManager.GetAppTheme("Base" + _persistentData.Options.Theme);
            ThemeManager.ChangeAppStyle(Application.Current, accent, theme);
            ((MenuItem)NameScope.GetNameScope(this).FindName("mnuViewAccent" + sAccent)).IsChecked = true;
            _persistentData.Options.Accent = sAccent;
        }
        #endregion

        #region Legacy

        /// <summary>
        /// Compares the AssemblyVersion against the one in PersistentData and makes
        /// nessary updates when versions don't match.
        /// </summary>
        private async Task CheckAppVersionAndDoNecessaryChanges()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            var productVersion = fvi.ProductVersion;
            var persistentDataVersion = _persistentData.AppVersion;
            if (productVersion == persistentDataVersion)
                return;
            if(string.IsNullOrEmpty(persistentDataVersion))
                await ImportLegacySavedBuilds();
            if (String.CompareOrdinal("2.2.4", persistentDataVersion) > 0)
                SetCurrentOpenBuildBasedOnName();

            _persistentData.AppVersion = productVersion;
        }

        private void SetCurrentOpenBuildBasedOnName()
        {
            var buildNameMatch =
                (from PoEBuild build in lvSavedBuilds.Items
                    where build.Name == _persistentData.CurrentBuild.Name
                    select build).FirstOrDefault();
            if (buildNameMatch != null)
            {
                foreach (PoEBuild item in lvSavedBuilds.Items)
                {
                    item.CurrentlyOpen = false;
                }
                buildNameMatch.CurrentlyOpen = true;
                lvSavedBuilds.Items.Refresh();
            }
        }

        /// <summary>
        /// Import builds from legacy build save file "savedBuilds" to PersistentData.xml.
        /// Warning: This will remove the "savedBuilds"
        /// </summary>
        private async Task ImportLegacySavedBuilds()
        {
            try
            {
                if (File.Exists("savedBuilds"))
                {
                    var saved_builds = new List<PoEBuild>();
                    var builds = File.ReadAllText("savedBuilds").Split('\n');
                    foreach (var b in builds)
                    {
                        var description = b.Split(';')[0].Split('|')[1];
                        var poeClass = description.Split(',')[0].Trim();
                        var pointsUsed = description.Split(',')[1].Trim().Split(' ')[0].Trim();

                        if (HasBuildNote(b))
                        {

                            saved_builds.Add(new PoEBuild(b.Split(';')[0].Split('|')[0], poeClass, pointsUsed,
                                b.Split(';')[1].Split('|')[0], b.Split(';')[1].Split('|')[1]));
                        }
                        else
                        {
                            saved_builds.Add(new PoEBuild(b.Split(';')[0].Split('|')[0], poeClass, pointsUsed,
                                b.Split(';')[1], ""));
                        }
                    }
                    lvSavedBuilds.Items.Clear();
                    foreach (var lvi in saved_builds)
                    {
                        lvSavedBuilds.Items.Add(lvi);
                    }
                    File.Move("savedBuilds", "savedBuilds.old");
                    await SaveBuildsToFile();
                }
            }
            catch (Exception ex)
            {
                await this.ShowErrorAsync(L10n.Message("An error occurred while attempting to load saved builds."), ex.Message);
            }
        }

        private static bool HasBuildNote(string b)
        {
            var buildNoteTest = b.Split(';')[1].Split('|');
            return buildNoteTest.Length > 1;
        }

        #endregion

        private void lvSavedBuilds_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel.Tree == null)
                return;

            var build = lvSavedBuilds.SelectedItem as PoEBuild;
            if (build != null && _persistentData.Options.TreeComparisonEnabled)
            {
                HashSet<ushort> nodes;
                int ctype;
                int atype;
                SkillTree.DecodeURL(build.Url, out nodes, out ctype, out atype);

                ViewModel.Tree.HighlightedNodes = nodes;
                int level;
                try
                {
                    level = int.Parse(build.Level);
                }
                catch
                {
                    level = 0;
                }
                ViewModel.Tree.HighlightedAttributes = SkillTree.GetAttributes(nodes, ctype, level, build.Bandits);
            }
            else
            {
                ViewModel.Tree.HighlightedNodes = null;
                ViewModel.Tree.HighlightedAttributes = null;
            }

            ViewModel.Tree.DrawTreeComparisonHighlight();
            UpdateUI();
        }

        private void ToggleTreeComparison_Click(object sender, RoutedEventArgs e)
        {
            lvSavedBuilds_SelectionChanged(null, null);
        }

        private async void Button_Craft_Click(object sender, RoutedEventArgs e)
        {
            var w = new CraftWindow(PersistentData.EquipmentData);
            await this.ShowDialogAsync(new CraftViewModel(), w);
            if (!w.DialogResult) return;

            var item = w.Item;
            if (PersistentData.StashItems.Count > 0)
                item.Y = PersistentData.StashItems.Max(i => i.Y + i.Height);

            Stash.Items.Add(item);

            Stash.AddHighlightRange(new IntRange { From = item.Y, Range = item.Height });
            Stash.asBar.Value = item.Y;
        }

        private static DragDropEffects deleteRect_DropEffect(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(DraggedItem)))
            {
                var draggedItem = (DraggedItem)e.Data.GetData(typeof(DraggedItem));
                var effect = draggedItem.DropOnBinEffect;

                if (e.AllowedEffects.HasFlag(effect))
                {
                    return effect;
                }
            }
            return DragDropEffects.None;
        }

        private void deleteRect_DragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = deleteRect_DropEffect(e);
        }

        private void deleteRect_Drop(object sender, DragEventArgs e)
        {
            var effect = deleteRect_DropEffect(e);
            if (effect == DragDropEffects.None)
                return;

            e.Handled = true;
            e.Effects = effect;
            var draggedItem = (DraggedItem)e.Data.GetData(typeof(DraggedItem));
            var visualizer = draggedItem.SourceItemVisualizer;
            var st = visualizer.TryFindParent<Stash>();
            if (st != null)
            {
                st.RemoveItem(visualizer.Item);
            }
            else
            {
                visualizer.Item = null;
            }
            deleteRect.Opacity = 0.0;
        }

        private void deleteRect_DragEnter(object sender, DragEventArgs e)
        {
            if (deleteRect_DropEffect(e) != DragDropEffects.None)
            {
                deleteRect.Opacity = 0.3;
            }
        }

        private void deleteRect_DragLeave(object sender, DragEventArgs e)
        {
            if (deleteRect_DropEffect(e) != DragDropEffects.None)
            {
                deleteRect.Opacity = 0.0;
            }
        }

        #region Async task helpers

        private void AsyncTaskStarted(string infoText)
        {
            NoAsyncTaskRunning = false;
            TitleStatusTextBlock.Text = infoText;
            TitleStatusButton.Visibility = Visibility.Visible;
        }

        private void AsyncTaskCompleted()
        {
            TitleStatusButton.Visibility = Visibility.Hidden;

            NoAsyncTaskRunning = true;
        }

        private async Task<TResult> AwaitAsyncTask<TResult>(string infoText, Task<TResult> task)
        {
            AsyncTaskStarted(infoText);
            try
            {
                return await task;
            }
            finally
            {
                AsyncTaskCompleted();
            }
        }

        private async Task AwaitAsyncTask(string infoText, Task task)
        {
            AsyncTaskStarted(infoText);
            try
            {
                await task;
            }
            finally
            {
                AsyncTaskCompleted();
            }
        }

        #endregion

        public override IWindowPlacementSettings GetWindowPlacementSettings()
        {
            var settings = base.GetWindowPlacementSettings();
            if (WindowPlacementSettings != null) return settings;

            // Settings just got created, give them a proper SettingsProvider.
            var appSettings = settings as ApplicationSettingsBase;
            if (appSettings == null)
            {
                // Nothing we can do here.
                return settings;
            }
            var provider = new CustomSettingsProvider(appSettings.SettingsKey);
            // This may look ugly, but it is needed and nulls are the only parameter
            // Initialize is ever called with by anything.
            provider.Initialize(null, null);
            appSettings.Providers.Add(provider);
            // Change the provider for each SettingsProperty.
            foreach (var property in appSettings.Properties)
            {
                ((SettingsProperty) property).Provider = provider;
            }
            appSettings.Reload();
            return settings;
        }
    }
}