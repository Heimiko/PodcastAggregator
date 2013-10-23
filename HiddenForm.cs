using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.IO;
using System.Diagnostics;

namespace PodcastAggregator
{
    public partial class HiddenForm : Form
    {
        public HiddenForm()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.Visible = false;
            timer1.Interval = 10000;    // wait 10 seconds before starting to check
            timer2.Interval = 10000;

#if DEBUG
            timer1_Tick(null, null);
            timer2_Tick(null, null);
#else
            timer1.Start();
            timer2.Start();
#endif
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            timer1.Stop();

            string FileName = "Podcasts.opml";
            FileName = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), FileName);

            Aggregator Agg = new Aggregator();
            Agg.ProcessOPML(FileName, true);

            timer1.Interval = 1800000;  // wait half an hour before next check
            timer1.Start();
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            timer2.Stop();

            string FileName = "NewsFeeds.opml";
            FileName = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), FileName);

            Aggregator Agg = new Aggregator();
            Agg.ProcessOPML(FileName, false);

            timer2.Interval = 1800000;  // wait 5 minutes fore next check
            timer2.Start();
        }
    }
}
