using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading.Tasks;

namespace PSSQT
{
    public enum ProgressType
    {
        Default,
        None,
        Host,
        Verbose
    }

    public interface IProgress
    {
        void ShowProgress(int startRow, int totalRows, int remaining);
    }

    public abstract class AbstractProgress : IProgress
    {
        public Cmdlet Cmdlet { get; set; }
        protected AbstractProgress(Cmdlet cmdlet)
        {
            Cmdlet = cmdlet;

        }

        public abstract void ShowProgress(int startRow, int totalRows, int remaining);
    }

    public class ProgressFactory
    {
        public static IProgress CreateProgressIndicator(ProgressType progressType, Cmdlet cmdlet)
        {
            switch (progressType)
            {
                case ProgressType.None:
                    return new NullProgress(cmdlet);

                case ProgressType.Host:
                    return new WriteHostProgress(cmdlet);

                case ProgressType.Verbose:
                    return new WriteVerboseProgress(cmdlet);

                default:
                    return new PSWriteProgress(cmdlet);
            }
        }
    }


    public class PSWriteProgress : AbstractProgress
    {
        public PSWriteProgress(Cmdlet cmdlet) : base(cmdlet)
        {
        }

        public override void ShowProgress(int startRow, int totalRows, int remaining)
        {
            if (remaining == 0)
            {
                var prec = new ProgressRecord(1, "Processing results...", "Starting");
                prec.PercentComplete = 0;

                Cmdlet.WriteProgress(prec);
            }
            else
            {
                var prec = new ProgressRecord(1, "Processing results...", String.Format(" {0} out of {1}", startRow, totalRows));
                prec.PercentComplete = 100 * startRow / totalRows;

                Cmdlet.WriteProgress(prec);
            }
        }
    }

    public class NullProgress : AbstractProgress
    {
        public NullProgress(Cmdlet cmdlet) : base(cmdlet)
        {
        }

        public override void ShowProgress(int startRow, int totalRows, int remaining)
        {
            // Do nothing
        }
    }

    public abstract class AbstractProgressHelper : AbstractProgress
    {
        private Stopwatch stopWatch = new Stopwatch();

        public DateTime StartTime { get; set; }

        public TimeSpan TotalTime
        {
            get
            {
                return DateTime.Now - StartTime;
            }
        }
        public AbstractProgressHelper(Cmdlet cmdlet) : base(cmdlet)
        {
            Reset();
        }

        protected void Reset()
        {
            StartTime = DateTime.Now;
            stopWatch.Reset();
        }


        public override void ShowProgress(int startRow, int totalRows, int remaining)
        {
            if (remaining == 0)
            {
                Reset();
                stopWatch.Start();

                ProgressStart(StartTime, startRow, totalRows, remaining);
            }
            else
            {
                ProgressContinue(stopWatch.Elapsed, startRow, totalRows, remaining);
                stopWatch.Restart();
            }

        }

        internal virtual void ProgressContinue(TimeSpan elapsed, int startRow, int totalRows, int remaining)
        {
            WriteProgress($"::: TotalTime: {TotalTime} LapTime: {elapsed} :::  StartRow: {startRow}, TotalRows: {totalRows}, Remaining: {remaining}");
        }

 
        internal virtual void ProgressStart(DateTime startTime, int startRow, int totalRows, int remaining)
        {
            WriteProgress($"::: {StartTime} :::");
        }

        internal abstract void WriteProgress(string v);
    }


    public class WriteHostProgress : AbstractProgressHelper
    {
        private static readonly string[] tags = new[] { "PSHOST" };

        public WriteHostProgress(Cmdlet cmdlet) : base(cmdlet)
        {

        }

        internal override void WriteProgress(string msg)
        {
            var himsg = new HostInformationMessage
            {
                Message = msg,
                ForegroundColor = ConsoleColor.DarkYellow,
                BackgroundColor = ConsoleColor.DarkBlue,
                NoNewLine = false
            };

            Cmdlet.WriteInformation(himsg, tags);
        }

    }

    public class WriteVerboseProgress : AbstractProgressHelper
    {
        public WriteVerboseProgress(Cmdlet cmdlet) : base(cmdlet)
        {
        }

        internal override void WriteProgress(string msg)
        {
            Cmdlet.WriteVerbose(msg);
        }

    }

}
