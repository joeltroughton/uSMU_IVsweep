using System;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;
using System.Collections.Generic;
using System.Windows.Input;
using System.IO;
using System.Text;
using Microsoft.Win32; //FileDialog
using WinForms = System.Windows.Forms; //FolderDialog
using System.Diagnostics; //Debug.WriteLine
using ScottPlot.plottables;
using ScottPlot;

namespace uSMU
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        SerialPort sp = new SerialPort();
        public string[] ports = SerialPort.GetPortNames();

        List<float> voltageList = new List<float>();
        List<float> currentList = new List<float>();

        double[] dvoltageArray;
        double[] dcurrentArray;

        List<double> dvoltageList = new List<double>();
        List<double> dcurrentList = new List<double>();

        List<float> powerList = new List<float>();

        bool replyRecieved = false;
        bool stopBtnPressed = false;

        DispatcherTimer stabilityTimer = new DispatcherTimer();
        DispatcherTimer countdownTimer = new DispatcherTimer();

        Stopwatch stopwatch = new Stopwatch();

        PlottableScatter jvplot;


        DispatcherTimer renderTimer = new DispatcherTimer();


        public MainWindow()
        {
            InitializeComponent();

            // Enumerate port list in box
            portsbox.ItemsSource = ports;
            start.IsEnabled = false;
            stop.IsEnabled = false;
            disconnect.IsEnabled = false;

            jvPlot.plt.YLabel("Current density (mA/cm²)");
            jvPlot.plt.XLabel("Voltage (V)");


            renderTimer.Interval = TimeSpan.FromMilliseconds(10);
            renderTimer.Tick += RenderGraph;



        }


        void timer_Tick(object sender, EventArgs e)
        {
            //lblTime.Content = DateTime.Now.ToLongTimeString();
            scheduledMeasurement();
        }



        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string portName = portsbox.Text;
                sp.PortName = portName;
                sp.BaudRate = 115200;
                sp.DtrEnable = true;
                sp.RtsEnable = true;
                sp.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);
                sp.Open();
                status.Text = "Connected to " + portName;
                disconnect.IsEnabled = true;
                connect.IsEnabled = false;
                portsbox.IsEnabled = false;
                start.IsEnabled = true;
                stop.IsEnabled = true;


            }
            catch (Exception)
            {
                MessageBox.Show("Connection failed. Is the correct port selected?");
            }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            sp.Close();
            disconnect.IsEnabled = false;
            connect.IsEnabled = true;
            portsbox.IsEnabled = true;
            start.IsEnabled = false;
            stop.IsEnabled = false;

            status.Text = "Disconnected";
        }


        private void Portsbox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            string levelInput = levelbox.Text;
            string sendString = "";


            sendString = String.Format("CH1:MEA:VOL {0}", levelInput.ToString());


            sp.WriteLine(sendString);
            Console.WriteLine(sendString);
            Task.Delay(100);
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            stopBtnPressed = true;
            sp.WriteLine("CH1:DIS");
        }

        private delegate void UpdateUiTextDelegate(string text);


        private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            string inLine = sp.ReadLine();
            //Dispatcher.Invoke(DispatcherPriority.Send, new UpdateUiTextDelegate(UpdateData), inLine);
            Dispatcher.Invoke(() =>
            {
                try
                {
                    UpdateData(inLine);
                    replyRecieved = true;

                }
                catch (Exception ex)
                {
                    //  MessageBox.Show("Error interpreting input data");
                    //  Console.WriteLine("UpdateData error - {0}", ex);
                }
            });
        }



        private async void UpdateData(string inLine)
        {
            Console.WriteLine("Recieved: {0}", inLine);


            string str = inLine.Substring(0, inLine.Length);
            str = str.Replace("\0", string.Empty);

            float activeArea = float.Parse(cellArea.Text);


            try
            {
                string[] parts = str.Split(',');

                voltage.Text = parts[0];
                current.Text = parts[1];

                addIVDataToDatagrid();
                voltageList.Add(float.Parse(voltage.Text));
                currentList.Add(((float.Parse(current.Text) * 1000) / activeArea));

                dvoltageList.Add(double.Parse(voltage.Text));
                dcurrentList.Add(((float.Parse(current.Text) * 1000) / activeArea));

                powerList.Add(((float.Parse(current.Text) * 1000 / activeArea)) * float.Parse(voltage.Text) * -1);

                dvoltageArray = dvoltageList.ToArray();
                dcurrentArray = dcurrentList.ToArray();
            }

            catch (Exception ex)
            {
                Console.WriteLine("Error - Data from SMU not understood. {0}", ex);
            }






        }
        private void addIVDataToDatagrid()
        {

            float activeArea = float.Parse(cellArea.Text);

            ivData tempData = new ivData();

            try
            {
                float voltageFloat = float.Parse(voltage.Text);
                float currentFloat = ((float.Parse(current.Text) * -1000) / activeArea);
                float powerFloat = voltageFloat * currentFloat;

                tempData.voltage = voltageFloat;
                tempData.current = currentFloat;
                tempData.power = powerFloat;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Serial data not understood");
                MessageBox.Show("Serial data not understood");
            }


            //tempData.power = powerFloat.ToString();



            ivDatagrid.Items.Add(tempData);

            dvoltageArray = dvoltageList.ToArray();
            dcurrentArray = dcurrentList.ToArray();


        }

        public class ivData
        {
            public double voltage { get; set; }
            public double current { get; set; }
            public double power { get; set; }

        }



        private void SweepBTN_Click(object sender, RoutedEventArgs e)
        {
            ivDatagrid.Items.Clear();
            voltageList.Clear();
            currentList.Clear();
            dvoltageList.Clear();
            dcurrentList.Clear();

            if (dvoltageArray != null)
            {
                Array.Clear(dvoltageArray, 0, dvoltageArray.Length);
                Array.Clear(dcurrentArray, 0, dcurrentArray.Length);
            }


            ClearGraph();

            renderTimer.Start();


            double startLevel = double.Parse(startVoltage.Text);
            double endLevel = double.Parse(endVoltage.Text);
            double stepLevel = double.Parse(vStepSize.Text) / 1000;

            int smuChannel = 1;

            int oversample_rate = int.Parse(osr.Text);

            string send = String.Format("CH1:OSR {0}", oversample_rate);
            sp.WriteLine(send);
            Console.WriteLine("Oversample rate set to {0}", oversample_rate);

            Dispatcher.Invoke(() =>
            {
                sendLevels(startLevel, endLevel, stepLevel, smuChannel);
            });




        }

        void Countdown(int count, TimeSpan interval, Action<int> ts, bool start)
        {
            var dt = new System.Windows.Threading.DispatcherTimer();
            dt.Interval = interval;
            dt.Tick += (_, a) =>
            {
                if (count-- == 0)
                    dt.Stop();
                else
                    ts(count);
            };
            ts(count);

            if (start == true)
            {
                dt.Start();
            }
            else if (start == false)
            {
                dt.Stop();
            }
        }

        private void StabBtnStart_Click(object sender, RoutedEventArgs e)
        {

            intervalMins.IsEnabled = false;
            ch1vocHold.IsEnabled = false;
            ch1jscHold.IsEnabled = false;
            ch1mppHold.IsEnabled = false;


            float stabIntervalSecs = float.Parse(intervalMins.Text) * 60;
            stabilityTimer.Interval = TimeSpan.FromSeconds(stabIntervalSecs);
            stabilityTimer.Tick += timer_Tick;
            stabilityTimer.Start();

            int istabIntervalSecs = (int)stabIntervalSecs;
            Countdown(istabIntervalSecs, TimeSpan.FromSeconds(1), cur => lblTime.Content = cur.ToString(), true);

        }

        private void StabBtnStop_Click(object sender, RoutedEventArgs e)
        {

            intervalMins.IsEnabled = true;
            ch1vocHold.IsEnabled = true;
            ch1jscHold.IsEnabled = true;
            ch1mppHold.IsEnabled = true;

            Countdown(0, TimeSpan.FromSeconds(1), cur => lblTime.Content = cur.ToString(), false);
            stabilityTimer.Stop();

        }



        private async Task scheduledMeasurement()
        {

            int istabIntervalSecs = (int)(float.Parse(intervalMins.Text) * 60);

            Countdown(istabIntervalSecs, TimeSpan.FromSeconds(1), cur => lblTime.Content = cur.ToString(), true);

            ClearGraph();
            ivDatagrid.Items.Clear();
            voltageList.Clear();
            currentList.Clear();
            dvoltageList.Clear();
            dcurrentList.Clear();

            double startLevel = double.Parse(startVoltage.Text);
            double endLevel = double.Parse(endVoltage.Text);
            double stepLevel = double.Parse(vStepSize.Text) / 1000;
            int smuChannel = 0;


            ClearGraph();
            ivDatagrid.Items.Clear();

            smuChannel = 1;

            var chSend = sendLevels(startLevel, endLevel, stepLevel, smuChannel);
            await chSend;

            exportData(1);
            await Task.Delay(500);



        }

        private void dwellCondition()
        {

            if (ch1mppHold.IsChecked ?? true)
            {
                double mpp = findMPP();
                String smuCommand = String.Format("CH1:VOL {0}", mpp);
                Console.WriteLine("Writing MPPV to CH1: {0} V", mpp);
                sp.WriteLine(smuCommand);
            }
            else if (ch1vocHold.IsChecked ?? true)
            {
                String smuCommand = String.Format("CH1:DIS");
                Console.WriteLine("Going open-circuit on CH1");
                sp.WriteLine(smuCommand);
            }
            else if (ch1jscHold.IsChecked ?? true)
            {
                String smuCommand = String.Format("CH1:VOL 0");
                Console.WriteLine("Going short-circuit on CH1");
                sp.WriteLine(smuCommand);
            }
            else
            {
                float arbHoldVoltage = float.Parse(ch1arbHold.Text);
                String smuCommand = String.Format("CH1:VOL {0}", arbHoldVoltage);
                Console.WriteLine("Applying {0} V to CH1", arbHoldVoltage);
                sp.WriteLine(smuCommand);
            }

        }


        private async Task sendLevels(double startLevel, double endLevel, double stepLevel, int smuChannel)
        {

            voltageList.Clear();
            currentList.Clear();
            dvoltageList.Clear();
            dcurrentList.Clear();
            powerList.Clear();

            ivDatagrid.Items.Clear();


            startVoltage.IsEnabled = false;
            endVoltage.IsEnabled = false;
            vStepSize.IsEnabled = false;
            delay.IsEnabled = false;
            Ilim.IsEnabled = false;



            int count = 0;

            int delayMillis = Int32.Parse(delay.Text);

            //float currentLimit = float.Parse(Ilim.Text) / 1000;
            float currentLimit = float.Parse(Ilim.Text);

            string send = String.Format("CH1:CUR {0}", currentLimit.ToString("G6"));
            sp.WriteLine(send);
            Console.WriteLine("Current limit sent: {0}", send);


            send = String.Format("CH1:ENA");
            sp.WriteLine(send);
            System.Threading.Thread.Sleep(200);

            send = String.Format("CH1:VOL {0}", startLevel);
            sp.WriteLine(send);
            System.Threading.Thread.Sleep(200);

            if (startLevel > endLevel)
            {

                for (double i = startLevel; i > endLevel; i -= stepLevel)
                {
                    send = String.Format("CH1:MEA:VOL {0}", i.ToString("G4"));

                    replyRecieved = false;
                    sp.WriteLine(send);
                    Console.WriteLine("Sending: {0}", send);
                    //updateGraph();

                    var stopwatch = new Stopwatch();
                    stopwatch.Start();

                    await Task.Delay(delayMillis);



                    while (!replyRecieved)
                    {
                        Console.WriteLine("Waiting...");
                        await Task.Delay(25);
                    }
                    replyRecieved = false;

                    stopwatch.Stop();
                    long elapsed_time = stopwatch.ElapsedMilliseconds;

                    Console.WriteLine("Measurement time: {0} ms", elapsed_time);

                }
            }
            else
            {
                for (double i = startLevel; i < endLevel; i += stepLevel)
                {
                    send = String.Format("CH1:MEA:VOL {0}", i.ToString("G4"));
                    replyRecieved = false;

                    sp.WriteLine(send);
                    Console.WriteLine("Sending: {0}", send);

                    var stopwatch = new Stopwatch();
                    stopwatch.Start();

                    await Task.Delay(delayMillis);




                    while (!replyRecieved)
                    {
                        Console.WriteLine("Waiting for serial data...");
                        await Task.Delay(25);
                    }

                    replyRecieved = false;


                    stopwatch.Stop();
                    long elapsed_time = stopwatch.ElapsedMilliseconds;

                    Console.WriteLine("Measurement time: {0} ms", elapsed_time);
                }
            }

            await Task.Delay(500);


            dvoltageArray = dvoltageList.ToArray();
            dcurrentArray = dcurrentList.ToArray();

            List<double> dvoltageListTemp = new List<double>();
            List<double> dcurrentListTemp = new List<double>();

            //updateGraphScott(dvoltageArray, dcurrentArray);

            try
            {
                voc.Text = findVOC().ToString("0.###");
                jsc.Text = findJSC().ToString("#.###");
                ff.Text = findFillFactor().ToString("0.###");
                pce.Text = findPCE().ToString("0.###");
                vmpp.Text = findMPP().ToString("0.###");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Problem calculating parameters in sendLevels fn: {0}", ex);
            }

            dwellCondition();
            renderTimer.Stop();


            startVoltage.IsEnabled = true;
            endVoltage.IsEnabled = true;
            vStepSize.IsEnabled = true;
            delay.IsEnabled = true;
            Ilim.IsEnabled = true;

        }





        public void ClearGraph_Click(object sender, RoutedEventArgs e)
        {
            ClearGraph();

        }

        public void ClearGraph()
        {
            if (dvoltageArray != null)
            {
                Array.Clear(dvoltageArray, 0, dvoltageArray.Length);
                Array.Clear(dcurrentArray, 0, dcurrentArray.Length);
            }


            jvPlot.plt.Clear();
            jvPlot.Render();

        }

        public void updateGraphScott(double[] voltage, double[] current)
        {
            jvPlot.plt.PlotScatter(voltage, current, color: System.Drawing.Color.Red, lineWidth: 4, markerSize: 10);
            jvPlot.plt.PlotHLine(0, color: System.Drawing.Color.Black, lineWidth: 1);
            jvPlot.plt.PlotVLine(0, color: System.Drawing.Color.Black, lineWidth: 1);
            jvPlot.plt.AxisAuto(0.1, 0.1); // no horizontal padding, 50% vertical padding
            jvPlot.Render();
        }

        void RenderGraph(object sender, EventArgs e)
        {
            if (dvoltageArray != null)
            {
                jvPlot.plt.Clear();

                jvPlot.plt.PlotScatter(dvoltageArray, dcurrentArray, color: System.Drawing.Color.Red, lineWidth: 4, markerSize: 10);
                jvPlot.plt.PlotHLine(0, color: System.Drawing.Color.Black, lineWidth: 1);
                jvPlot.plt.PlotVLine(0, color: System.Drawing.Color.Black, lineWidth: 1);
                jvPlot.plt.AxisAuto(0.1, 0.1); // no horizontal padding, 50% vertical padding
                jvPlot.Render(skipIfCurrentlyRendering: true);
            }
        }


        private void Export_Click(object sender, RoutedEventArgs e)
        {
            exportData(1);

        }

        public void exportData(int smuChannel)
        {
            ivDatagrid.SelectAllCells();
            ivDatagrid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
            ApplicationCommands.Copy.Execute(null, ivDatagrid);
            ivDatagrid.UnselectAllCells();
            String result = (string)Clipboard.GetData(DataFormats.CommaSeparatedValue);
            int epoch = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;


            String filename = saveDirectoryText.Text;
            filename += String.Format("/JVs");

            DirectoryInfo di = Directory.CreateDirectory(filename);

            filename += String.Format("/SMU{0} - {1}.txt", smuChannel, epoch);

            File.WriteAllText(filename, result, UnicodeEncoding.UTF8);

            var stats = String.Format("\n{0},{1},{2},{3},{4},{5}", epoch, voc.Text, jsc.Text, ff.Text, pce.Text, vmpp.Text);


            String statsFilename = saveDirectoryText.Text;
            statsFilename += String.Format("/SMU{0} - stats.csv", smuChannel);


            if (!File.Exists(statsFilename))
            {
                var statsTopLine = String.Format("Time (epoch),VOC (V),JSC (mA/cm2),Fill factor,PCE (%),VMPP (V)", epoch, voc.Text, jsc.Text, ff.Text, pce.Text, vmpp.Text);
                File.AppendAllText(statsFilename, statsTopLine, UnicodeEncoding.UTF8);
                File.AppendAllText(statsFilename, stats, UnicodeEncoding.UTF8);

            }
            else
            {
                File.AppendAllText(statsFilename, stats, UnicodeEncoding.UTF8);
            }
        }


        public float findVOC()
        {

            float[] vocCheckCurrentArray = currentList.ToArray();
            float[] vocCheckVoltageArray = voltageList.ToArray();

            // If the current at the start of the array is positive, reverse the array.
            // We always want -ve currents first for VOC finding
            if (vocCheckCurrentArray[0] < 0)
            {
                Array.Reverse(vocCheckCurrentArray);
                Array.Reverse(vocCheckVoltageArray);
            }

            float lastPositiveJ = 0;
            float lastPositiveV = 0;

            float firstNegativeJ = 0;
            float firstNegativeV = 0;

            int lastPositiveIndex = 0;

            foreach (float element in vocCheckCurrentArray)
            {
                if (element > 0)
                {
                    lastPositiveJ = element;
                    lastPositiveIndex = Array.IndexOf(vocCheckCurrentArray, element);

                    lastPositiveV = vocCheckVoltageArray[lastPositiveIndex];

                    firstNegativeJ = vocCheckCurrentArray[lastPositiveIndex + 1];
                    firstNegativeV = vocCheckVoltageArray[lastPositiveIndex + 1];

                }
            }

            float vocSlope = (firstNegativeJ - lastPositiveJ) / (firstNegativeV - lastPositiveV);
            float jIntercept = lastPositiveJ - (vocSlope * lastPositiveV);
            float voc = (0 - jIntercept) / vocSlope;

            //Console.WriteLine("Last positive: Current = {0}, Voltage = {1}", lastPositiveJ, lastPositiveV);
            //Console.WriteLine("First negative: Current = {0}, Voltage = {1}", firstNegativeJ, firstNegativeV);
            //Console.WriteLine("VOC = {0}", voc);
            return Math.Abs(voc);
        }
        public float findJSC()
        {
            float[] jscCheckCurrentArray = currentList.ToArray();
            float[] jscCheckVoltageArray = voltageList.ToArray();

            if (jscCheckVoltageArray[0] < 0)
            {
                Array.Reverse(jscCheckVoltageArray);
                Array.Reverse(jscCheckCurrentArray);
            }

            float lastNegativeV = 0;
            float lastNegativeJ = 0;

            float firstPositiveV = 0;
            float firstPositiveJ = 0;

            int lastNegativeIndex = 0;


            foreach (float element in jscCheckVoltageArray)
            {
                if (element > 0)
                {
                    lastNegativeV = element;
                    lastNegativeIndex = Array.IndexOf(jscCheckVoltageArray, element);
                    lastNegativeJ = jscCheckCurrentArray[lastNegativeIndex];

                    firstPositiveV = jscCheckVoltageArray[lastNegativeIndex + 1];
                    firstPositiveJ = jscCheckCurrentArray[lastNegativeIndex + 1];
                }
            }

            float jscSlope = (firstPositiveV - lastNegativeV) / (firstPositiveJ - lastNegativeJ);
            float vIntercept = lastNegativeV - (jscSlope * lastNegativeJ);
            float jsc = (0 - vIntercept) / jscSlope;

            return Math.Abs(jsc);

        }

        public double findFillFactor()
        {
            float[] powerArray = powerList.ToArray();

            try
            {
                float maxPowerValue = powerArray.Max();
                int maxPowerIndex = powerArray.ToList().IndexOf(maxPowerValue);

                double fillfactor = Math.Abs(maxPowerValue) / (Math.Abs(findVOC()) * Math.Abs(findJSC()));

                Console.WriteLine("MP: {0}, VOC: {1}, JSC: {2}, FF: {3}", maxPowerValue, Math.Abs(findVOC()), Math.Abs(findJSC()), fillfactor);
                return fillfactor;


            }
            catch (Exception ex)
            {
                Console.WriteLine("Problem calculating FF: {0}", ex);
                return 0;
            }


        }

        public double findPCE()
        {
            float JSC = float.Parse(jsc.Text);
            float VOC = float.Parse(voc.Text);
            double FF = float.Parse(ff.Text);
            double PCE = JSC * VOC * FF;


            Console.WriteLine("VOC = {0}, JSC = {1}, FF = {2}, PCE = {3}", VOC, JSC, FF, PCE);
            return PCE;
        }


        public double findMPP()
        {

            try
            {
                float[] voltageArray = voltageList.ToArray();
                float[] powerArray = powerList.ToArray();


                float maxPowerValue = powerArray.Max();
                Console.WriteLine("Max power is {0} mA", maxPowerValue);
                int maxPowerIndex = powerArray.ToList().IndexOf(maxPowerValue);
                Console.WriteLine("Max power point voltage is {0} V", voltageArray[maxPowerIndex]);

                return voltageArray[maxPowerIndex];
            }

            catch (Exception ex)
            {
                Console.WriteLine("Error trying to find MPP: {0}", ex);
                return 0;
            }

        }

        private void SaveDirectory_Click(object sender, RoutedEventArgs e)
        {

            WinForms.FolderBrowserDialog folderDialog = new WinForms.FolderBrowserDialog();
            folderDialog.ShowNewFolderButton = true;
            folderDialog.SelectedPath = System.AppDomain.CurrentDomain.BaseDirectory;
            WinForms.DialogResult result = folderDialog.ShowDialog();

            if (result == WinForms.DialogResult.OK)
            {
                //----< Selected Folder >----
                //< Selected Path >
                String sPath = folderDialog.SelectedPath;
                Console.WriteLine(sPath);
                saveDirectoryText.Text = sPath;
                //</ Selected Path >

            }


        }

    }

}

