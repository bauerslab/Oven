using Syncfusion.UI.Xaml.Controls.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace OvenHMI
{
    public sealed partial class MainPage : Page
    {
        //Constants
        const int MaxSteps = 15;
        const int MinSteps = 2;
        const string RecipeFilenameExtension = ".recipe";

        //ApplicationData shortcuts
        StorageFolder LocalFolder => ApplicationData.Current.LocalFolder;
        public static IPropertySet Settings => ApplicationData.Current.LocalSettings.Values;

        Recipe Recipe { get; set; } = new Recipe();
        ObservableCollection<string> RecipeFiles = new ObservableCollection<string>();
        ObservableCollection<Sample> SampleData { get; set; } = new ObservableCollection<Sample>();

        /// <summary>Timer for periodic sample polling</summary>
        DispatcherTimer SamplePoll;
        /// <summary>Timer for screen lock timeout</summary>
        DispatcherTimer ScreenLockTimer;
        SemaphoreSlim DialogAvailability = new SemaphoreSlim(1, 1);

        public MainPage()
        {
            InitializeComponent();
            
            //Generate placeholder recipe
            Recipe.Steps.Add(new TemperatureTime { Time = TimeSpan.Zero, Temperature = 25 });
            Recipe.Steps.Add(new TemperatureTime { Time = TimeSpan.FromMinutes(30), Temperature = 225 });
            Recipe.Steps.Add(new TemperatureTime { Time = TimeSpan.FromHours(2), Temperature = 225 });
            Recipe.Steps.Add(new TemperatureTime { Time = TimeSpan.FromHours(3), Temperature = 25 });
            Recipe.Steps.CollectionChanged += Steps_CollectionChanged;

            //Initialize Timers
            SamplePoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            SamplePoll.Tick += SamplePoll_Tick;
            ScreenLockTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            ScreenLockTimer.Tick += ScreenLockTimer_Tick;
        }



        /*Main Page*/
        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            //Select the first step in the Recipe editor
            StepSelector.SelectedIndex = 0;
            
            //Get current oven status
            var status = await Oven.GetStatus();
            Status.Text = status.ToString();

            //Restart if the oven tells us to
            if (status == OvenStatus.NeedRestart)
            {
                ShutdownManager.BeginShutdown(ShutdownKind.Restart, TimeSpan.Zero);
                return;
            }

            //Start sample timer
            SamplePoll.Start();

            //Look for recipe files and populate recipe list
            var files = (await LocalFolder.GetFilesAsync())
                .Where(x => x.Name.EndsWith(RecipeFilenameExtension));
            if (files.Any())
            {
                foreach (var filename in files
                    .Select(x => x.Name.Remove(x.Name.Length - RecipeFilenameExtension.Length)))
                    RecipeFiles.Add(filename);
            }
            
            //Read PID coefficients from stored settings
            if (Settings.ContainsKey(nameof(PID)) && Settings[nameof(PID)] is string pidString)
            {
                var pid = PID.FromString(pidString);
                PInput.Value = pid.Proportional;
                IInput.Value = pid.Integral;
                DInput.Value = pid.Derivative;
                SendPID_Click(this, new RoutedEventArgs());
            }
        }
        private void Steps_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {   //Enable the send recipe button if there are any changes to the Recipe
            SendRecipe.IsEnabled = true;
        }


        /*Lock Screen*/
        private void ScreenLockTimer_Tick(object sender, object e)
        {   //Lock the screen after timeout
            ScreenLockTimer.Stop();
            LockScreen.Visibility = Visibility.Visible;
        }
        private void LockScreen_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {   //Swipe to unlock lock screen
            if (Math.Abs(e.Cumulative.Translation.X) + Math.Abs(e.Cumulative.Translation.Y) > 500)
                LockScreen.Visibility = Visibility.Collapsed;
        }
        private void LockScreen_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {   //Unlocking the screen resets the screen lock timer
            ScreenLockTimer.Start();
        }
        private void Page_Pointer_Input(object sender, PointerRoutedEventArgs e)
        {   //Any Mouse input resets screen lock timer
            if (LockScreen.Visibility == Visibility.Collapsed)
            {
                ScreenLockTimer.Stop();
                ScreenLockTimer.Start();
            }
        }
        private void Page_Keyboard_Input(object sender, KeyRoutedEventArgs e)
        {   //Any Keyboard input resets screen lock timer
            if (LockScreen.Visibility == Visibility.Collapsed)
            {
                ScreenLockTimer.Stop();
                ScreenLockTimer.Start();
            }
        }


        /*Popup Dialogs*/
        private async Task<ContentDialogResult> ShowError(string errorMessage) => await ShowDialog(new ContentDialog
        {
            Title = "Error",
            Background = new SolidColorBrush(Color.FromArgb(0, 0xFF, 0, 0x77)),
            Content = errorMessage,
            CloseButtonText = "Okay"
        });
        private async Task<bool> ShowConfirmation(string message) => ContentDialogResult.Primary == await ShowDialog(new ContentDialog
        {
            Title = "Confirm",
            Content = message,
            PrimaryButtonText = "Yes",
            SecondaryButtonText = "No"
        });
        private async Task<ContentDialogResult> ShowSuccess(string message) => await ShowDialog(new ContentDialog
        {
            Title = "Success",
            Content = message,
            CloseButtonText = "Okay"
        });
        /// <summary>Wrapper for showing only one dialog at a time and forming a queue</summary>
        private async Task<ContentDialogResult> ShowDialog(ContentDialog dialog)
        {
            await DialogAvailability.WaitAsync();
            var result = await dialog.ShowAsync();
            DialogAvailability.Release();
            return result;
        }


        /*Operations*/
        private async void SamplePoll_Tick(object sender, object e)
        {   //Periodically poll for current data

            //Get current sample
            Sample sample = await Oven.GetCurrentSample();
            UpdateSampleUI(sample);

            //Add sample to graph
            if (sample != null)
                SampleData.Add(sample);

            //Get the oven's current status code
            var status = await Oven.GetStatus();
            if (status == OvenStatus.NotConnected && Oven.ErrorMessage != null)
                Status.Text = Oven.ErrorMessage;
            else
                Status.Text = status.ToString();
        }
        private async void Start_Click(object sender, RoutedEventArgs e)
        {   //Send Start command to oven
            Start.IsEnabled = false;
            Status.Text = (await Oven.Start()).ToString();
            SampleData.Clear();
            Start.IsEnabled = true;
        }
        private async void Stop_Click(object sender, RoutedEventArgs e)
        {   //Send the stop command and save sample data to a csv file
            Stop.IsEnabled = false;
            Status.Text = (await Oven.Stop()).ToString();

            try
            {
                var filename = $"{DateTime.Now:yyyy-MM-dd HHmmss}.csv";
                var file = await DownloadsFolder.CreateFileAsync(filename);
                var lines = new List<string>{ "RealTime,RecipeTime (s),Temperature (°C),Ambient Temp (°C),Set Power (W)" };
                foreach (var sample in SampleData)
                    lines.Add($"{sample.RealTime:yyyy-MM-dd HH:mm:ss},{sample.Time},{sample.Temperature},{sample.Ambient},{sample.Power}");
                await FileIO.WriteLinesAsync(file, lines);
                await CachedFileManager.CompleteUpdatesAsync(file);
            }
            catch(Exception x)
            {
                await ShowError($"Error saving data file:{Environment.NewLine}{x.Message}");
            }

            Stop.IsEnabled = true;
        }
        private async void SendRecipe_Click(object sender, RoutedEventArgs e)
        {   //Send the current recipe to the oven
            SendRecipe.IsEnabled = false;
            if (Recipe.Steps.Count < MinSteps)
            {
                await ShowError($"Recipe has too few steps. Minimum is {MinSteps}.");
                SendRecipe.IsEnabled = true;
                return;
            }

            if (await Oven.SetRecipe(Recipe))
                await ShowSuccess("Recipe sent successfully.");
            else
                await ShowError($"Recipe failed to send.{(Oven.ErrorMessage != null ? Environment.NewLine + Oven.ErrorMessage : "")}");
            SendRecipe.IsEnabled = true;
        }
        private async void SendAmbient_Click(object sender, RoutedEventArgs e)
        {   //Send set ambient temperature to the oven
            SendAmbient.IsEnabled = false;

            if (await Oven.SetAmbient(Ambient.ValueFloat()))
                await ShowSuccess("Ambient temperature sent successfully.");
            else
                await ShowError($"Ambient temperature failed to send.{(Oven.ErrorMessage != null ? Environment.NewLine + Oven.ErrorMessage : "")}");

            SendAmbient.IsEnabled = true;
        }
        /// <summary>Update the UI with given sample data</summary>
        private void UpdateSampleUI(Sample latestSample)
        {
            Wattage.Text = $"{latestSample?.Power.ToString("0") ?? "N/A"} W";
            Temperature.Text = $"{latestSample?.Temperature.ToString("0") ?? "N/A"}°C";
            AmbientLabel.Text = $"Ambient({latestSample?.Ambient.ToString("0") ?? "N/A"}°C)";
        }


        /*Recipe Edit*/
        private void AddStep_Click(object sender, RoutedEventArgs e)
        {   //Add a new step 30mins after the last step
            Recipe.Steps.Add(new TemperatureTime { Time = Recipe.Steps.Max(x => x.Time) + TimeSpan.FromMinutes(30) });
            if (Recipe.Steps.Count >= MaxSteps)
                AddStep.IsEnabled = false;
            RemoveStep.IsEnabled = true;
        }
        private void RemoveStep_Click(object sender, RoutedEventArgs e)
        {   //Remove currently selected step
            if (StepSelector.SelectedItem is TemperatureTime item)
            {
                Recipe.Steps.Remove(item);
                if (Recipe.Steps.Count <= MinSteps)
                    RemoveStep.IsEnabled = false;
                AddStep.IsEnabled = true;
            }
        }
        private void StepSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {   //Update time and temperature values to reflect newly selected step
            StepTime.Value = CurrentStep.Time;
            StepTemperature.Value = CurrentStep.Temperature;
        }
        private void StepTemperature_ValueChanged(object sender, ValueChangedEventArgs e)
        {   //Update the temperature of the current step and change all other selected steps' temperatures by the same amount

            //If there is no step selected, don't do anything
            if (!(StepSelector?.SelectedIndex > -1))
                return;

            //Try to get value from control
            if (!StepTemperature.TryValueFloat(out float newValue))
                return;

            //Calculate difference and apply to all selected steps
            float difference = newValue - CurrentStep.Temperature;
            foreach (var selectedItem in StepSelector.SelectedItems)
                if (selectedItem is TemperatureTime step)
                    step.Temperature += difference;
        }
        private void StepTime_ValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {   //Update the time of the current step and change all other selected steps' times by the same amount

            //If there is no step selected, don't do anything
            if (!(StepSelector?.SelectedIndex > -1))
                return;

            //Try to get value from control
            if (!StepTime.TryValueTimeSpan(out TimeSpan newTime))
                return;

            //Calculate difference and apply to all selected steps
            TimeSpan difference = newTime - CurrentStep.Time;
            foreach (var selectedItem in StepSelector.SelectedItems)
                if (selectedItem is TemperatureTime step)
                    step.Time += difference;
        }
        /// <summary>The step selected in the StepSelector list</summary>
        TemperatureTime CurrentStep
        {
            get
            {
                if (!(StepSelector?.SelectedIndex > -1))
                    return new TemperatureTime();
                return Recipe.Steps[StepSelector.SelectedIndex];
            }
        }


        /*Recipe Save*/
        private async void SaveRecipe_Click(object sender, RoutedEventArgs e)
        {   //Save file, prompt for overwrite if it already exists
            try
            {
                StorageFile file;
                if (await LocalFolder.TryGetItemAsync(CurrentFilename) is StorageFile alreadyExists)
                {
                    if (!await ShowConfirmation($"Are you sure you want to overwrite \"{CurrentFilename}\"?"))
                        return;
                    file = alreadyExists;
                }
                else
                {
                    file = await LocalFolder.CreateFileAsync(CurrentFilename);
                    RecipeFiles.Add(CurrentFilename.Remove(CurrentFilename.Length - RecipeFilenameExtension.Length));
                }
                using (var stream = await file.OpenStreamForWriteAsync())
                    RecipeSerializer.Serialize(stream, Recipe);
            }
            catch (Exception x)
            {
                await ShowError(x.Message);
            }
        }
        private async void LoadRecipe_Click(object sender, RoutedEventArgs e)
        {   //Load currently selected recipe file
            if (RecipeSelector.SelectedIndex == -1)
                return;
            try
            {
                var file = await LocalFolder.GetFileAsync(CurrentFilename);
                Recipe r = RecipeSerializer.Deserialize(await file.OpenStreamForReadAsync()) as Recipe;
                if (r?.Steps?.Count >= MinSteps)
                {
                    Recipe.Steps.Clear();
                    foreach(var step in r.Steps)
                        Recipe.Steps.Add(step);
                }
                else
                {
                    await ShowError("Could not parse recipe file.");
                }
            }
            catch (Exception x)
            {
                await ShowError(x.Message);
            }
        }
        private async void DeleteRecipe_Click(object sender, RoutedEventArgs e)
        {   //Delete currently selected recipe file
            if (RecipeSelector.SelectedIndex == -1)
                return;

            if (await ShowConfirmation($"Are you sure you want to delete \"{CurrentFilename}\"?"))
            {
                try
                {
                    var file = await LocalFolder.GetFileAsync(RecipeSelector.SelectedItem.ToString());
                    await file?.DeleteAsync();
                    RecipeFiles.Remove(CurrentFilename);
                }
                catch(Exception x)
                {
                    await ShowError(x.Message);
                }
            }
        }
        private void RecipeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {   //Update UI to reflect newly selected recipe
            RecipeName.Text = RecipeSelector.SelectedItem?.ToString() ?? RecipeName.Text;
            DeleteRecipe.IsEnabled = LoadRecipe.IsEnabled = RecipeSelector.SelectedIndex != -1;
        }
        private void RecipeName_TextChanged(object sender, TextChangedEventArgs e)
        {   //If recipe name matches a recipe that already exists select it
            if (RecipeFiles.Contains(RecipeName.Text))
            {
                RecipeSelector.SelectedItem = RecipeName.Text;
                DeleteRecipe.IsEnabled = true;
                LoadRecipe.IsEnabled = true;
            }
            else
            {
                RecipeSelector.SelectedIndex = -1;
                DeleteRecipe.IsEnabled = false;
                LoadRecipe.IsEnabled = false;
            }
        }
        /// <summary>The filename of the file selected in the RecipeSelector list or the new filename if none selected</summary>
        string CurrentFilename
        {
            get
            {
                if (RecipeSelector.SelectedIndex != -1)
                    return RecipeSelector.SelectedItem.ToString() + RecipeFilenameExtension;
                else
                    return RecipeName.Text + RecipeFilenameExtension;
            }
        }
        /// <summary>Converts a recipe object to XML file or vice versa</summary>
        XmlSerializer RecipeSerializer = new XmlSerializer(typeof(Recipe));


        /*PID*/
        private async void SendPID_Click(object sender, RoutedEventArgs e)
        {   //Send PID coefficients to Oven
            SendPID.IsEnabled = false;

            PID hmi = (PInput.ValueFloat(), IInput.ValueFloat(), DInput.ValueFloat());
            PID psoc = await Oven.SetPID(hmi);
            if (psoc == hmi)
            {
                POven.Text = psoc.Proportional.ToString();
                IOven.Text = psoc.Integral.ToString();
                DOven.Text = psoc.Derivative.ToString();
                await ShowSuccess("PID updated successfully");
                Settings[nameof(PID)] = psoc.ToString();
            }
            
            SendPID.IsEnabled = true;
        }
        private async void RefreshPID_Click(object sender, RoutedEventArgs e)
        {   //Get Oven's current PID coefficients
            RefreshPID.IsEnabled = false;
            PID psoc = await Oven.GetPID();
            POven.Text = psoc.Proportional.ToString();
            IOven.Text = psoc.Integral.ToString();
            DOven.Text = psoc.Derivative.ToString();
            RefreshPID.IsEnabled = true;
        }

    }
}
