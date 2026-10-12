using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace VideoSplitter {
    public sealed class Engine {
        public volatile bool Cancelled;
        public int TotalClips;
        public Action<string> Log = delegate {};
        public Action<int> Progress = delegate {};
        readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        static string N(double n) { return n.ToString("0.######", CultureInfo.InvariantCulture); }
        public static string Quote(string s) {
            var b = new StringBuilder("\""); int slashes = 0;
            foreach(char c in s) {
                if(c == '\\') { slashes++; continue; }
                if(c == '"') { b.Append('\\', slashes * 2 + 1); b.Append(c); }
                else { b.Append('\\', slashes); b.Append(c); }
                slashes = 0;
            }
            b.Append('\\', slashes * 2); b.Append('"'); return b.ToString();
        }
        public void CheckTools() {
            foreach(string name in new [] {"ffmpeg.exe", "ffprobe.exe"})
                if(!File.Exists(Path.Combine(root, name))) throw new Exception("缺少 " + name + "。请完整解压软件包后运行。");
        }
        string Run(string exe, string args) {
            if(Cancelled) throw new OperationCanceledException();
            var info = new ProcessStartInfo(Path.Combine(root, exe), args);
            info.UseShellExecute = false; info.CreateNoWindow = true;
            info.RedirectStandardError = true; info.RedirectStandardOutput = true;
            info.StandardErrorEncoding = Encoding.UTF8; info.StandardOutputEncoding = Encoding.UTF8;
            using(var p = Process.Start(info)) {
                var output = p.StandardOutput.ReadToEndAsync();
                var errors = p.StandardError.ReadToEndAsync();
                while(!p.WaitForExit(100)) {
                    if(Cancelled) { try { p.Kill(); } catch {} p.WaitForExit();
                        Task.WaitAll(output, errors); throw new OperationCanceledException(); }
                }
                Task.WaitAll(output, errors);
                if(Cancelled) throw new OperationCanceledException();
                if(p.ExitCode != 0) throw new Exception(errors.Result.Trim());
                return output.Result;
            }
        }
        public double Duration(string input) {
            string json = Run("ffprobe.exe", "-v error -select_streams V:0 -show_entries stream=duration:format=duration -of json " + Quote(input));
            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var streams = (System.Collections.IList)data["streams"];
            if(streams.Count == 0) throw new Exception("文件中没有可处理的视频轨道。");
            var stream = (Dictionary<string, object>)streams[0];
            double d;
            if(stream.ContainsKey("duration") && double.TryParse(Convert.ToString(stream["duration"]), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0 && !double.IsInfinity(d)) return d;
            var format = (Dictionary<string, object>)data["format"];
            if(format.ContainsKey("duration") && double.TryParse(Convert.ToString(format["duration"]), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0 && !double.IsInfinity(d)) return d;
            throw new Exception("无法获取视频时长；文件可能损坏或不支持随机定位。");
        }
        public int Split(string input, string outputRoot, double seconds, bool keepTail) {
            if(double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.1 || seconds > 86400) throw new Exception("分段时长须在 0.1 至 86400 秒之间。");
            CheckTools();
            double duration = Duration(input);
            int full = (int)Math.Floor((duration + 0.000001) / seconds);
            double remainder = duration - full * seconds;
            int count = full + (keepTail && remainder > 0.000001 ? 1 : 0);
            if(count == 0) { Log("跳过：" + Path.GetFileName(input) + "（短于一段，已选择丢弃尾段）"); return 0; }
            Directory.CreateDirectory(outputRoot);
            string stem = Path.GetFileNameWithoutExtension(input);
            if(stem.Length > 60) stem = stem.Substring(0,60);
            string dir = Path.Combine(outputRoot, stem + "_" + N(seconds) + "s_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0,8));
            Directory.CreateDirectory(dir);
            string logPath = Path.Combine(dir, "处理记录.txt");
            File.WriteAllText(logPath, "原视频：" + input + "\r\n时长：" + N(duration) + " 秒\r\n分段：" + N(seconds) + " 秒；保留尾段：" + keepTail + "\r\n", Encoding.UTF8);
            Log(Path.GetFileName(input) + " · " + N(duration) + " 秒 → " + count + " 段");
            for(int i=0; i<count; i++) {
                if(Cancelled) throw new OperationCanceledException();
                double start = i * (double)seconds;
                double length = Math.Min(seconds, duration - start);
                string filename = "片段_" + (i+1).ToString("D3") + ".mp4";
                string target = Path.Combine(dir, filename);
                string temp = Path.Combine(dir, "片段_" + (i+1).ToString("D3") + ".part.mp4");
                try {
                    Log("正在生成 " + filename + "（" + N(start) + "–" + N(start+length) + " 秒）");
                    Run("ffmpeg.exe", "-hide_banner -loglevel error -nostdin -n -ss " + N(start) + " -i " + Quote(input) + " -t " + N(length) + " -map 0:V:0 -map 0:a:0? -sn -dn -map_metadata -1 -map_chapters -1 -vf " + Quote("pad=ceil(iw/2)*2:ceil(ih/2)*2") + " -c:v libx264 -preset fast -crf 20 -pix_fmt yuv420p -c:a aac -b:a 160k -movflags +faststart " + Quote(temp));
                    if(!File.Exists(temp) || new FileInfo(temp).Length == 0) throw new Exception("输出文件为空。");
                    File.Move(temp, target);
                    TotalClips++;
                    File.AppendAllText(logPath, filename + "：" + N(start) + "–" + N(start+length) + " 秒，完成\r\n", Encoding.UTF8);
                    Progress((i+1)*100/count);
                } catch(Exception ex) {
                    try { if(File.Exists(temp)) File.Delete(temp); } catch {}
                    File.AppendAllText(logPath, "中止：" + ex.Message + "\r\n", Encoding.UTF8); throw;
                }
            }
            Log("已保存到：" + dir); return count;
        }
    }

    public class MainForm : Form {
        readonly ListBox files = new ListBox();
        readonly TextBox output = new TextBox();
        readonly NumericUpDown duration = new NumericUpDown();
        readonly CheckBox tail = new CheckBox();
        readonly TextBox log = new TextBox();
        readonly ProgressBar progress = new ProgressBar();
        readonly Label state = new Label();
        readonly Button start = new Button(), cancel = new Button();
        readonly List<Control> editable = new List<Control>();
        Engine engine; bool busy;
        public MainForm() {
            Text = "视频分段工具 1.1"; ClientSize = new Size(800, 660);
            MinimumSize = new Size(816, 699); StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(246,248,252);
            Label title = LabelAt("视频分段", 24, 18, 500, 36); title.Font = new Font(Font.FontFamily, 21, FontStyle.Bold);
            LabelAt("批量导入 · 自定义分段秒数 · 导出 MP4",24,62,700,25);
            LabelAt("1  添加视频",24,106,300,26);
            Button add = ButtonAt("添加视频", 554,101,105,34); add.Click += delegate { PickFiles(); }; editable.Add(add);
            Button remove = ButtonAt("移除所选", 670,101,106,34); remove.Click += delegate {
                while(files.SelectedIndices.Count > 0) files.Items.RemoveAt(files.SelectedIndices[0]);
            }; editable.Add(remove);
            files.SetBounds(24,144,752,142); files.SelectionMode = SelectionMode.MultiExtended;
            files.HorizontalScrollbar = true; files.AllowDrop = true;
            files.DragEnter += delegate(object sender, DragEventArgs e) { if(!busy && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            files.DragDrop += delegate(object sender, DragEventArgs e) { AddFiles((string[])e.Data.GetData(DataFormats.FileDrop)); };
            Controls.Add(files); editable.Add(files);
            LabelAt("2  设置分段",24,299,260,28);
            duration.SetBounds(24,337,126,30); duration.Minimum=0.1m; duration.Maximum=86400m;
            duration.DecimalPlaces=2; duration.Increment=1m; duration.Value=15m;
            Controls.Add(duration); editable.Add(duration);
            LabelAt("秒",157,340,26,26);
            Button six=ButtonAt("6 秒",193,335,75,32); six.Click+=delegate { duration.Value=6m; }; editable.Add(six);
            Button fifteen=ButtonAt("15 秒",279,335,75,32); fifteen.Click+=delegate { duration.Value=15m; }; editable.Add(fifteen);
            tail.Text = "保留最后不足一段的片段"; tail.Checked = true; tail.SetBounds(382,337,380,30);
            Controls.Add(tail); editable.Add(tail);
            LabelAt("3  选择输出文件夹",24,380,300,28);
            output.SetBounds(24,419,635,30); output.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "视频分段");
            Controls.Add(output); editable.Add(output);
            Button browse = ButtonAt("浏览…",670,416,106,34); browse.Click += delegate {
                using(var d = new FolderBrowserDialog()) { d.Description = "选择片段保存位置"; if(Directory.Exists(output.Text)) d.SelectedPath=output.Text; if(d.ShowDialog()==DialogResult.OK) output.Text=d.SelectedPath; }
            }; editable.Add(browse);
            start.Text="开始分段"; start.SetBounds(24,468,150,40); start.BackColor=Color.FromArgb(45,92,222); start.ForeColor=Color.White;
            start.FlatStyle=FlatStyle.Flat; start.Click += async delegate { await StartBatch(); }; Controls.Add(start);
            cancel.Text="取消"; cancel.SetBounds(185,468,100,40); cancel.Enabled=false; cancel.Click += delegate { if(engine!=null) { engine.Cancelled=true; state.Text="正在取消…"; } }; Controls.Add(cancel);
            Button open = ButtonAt("打开输出文件夹",606,468,170,40); open.Click += delegate {
                try { if(Directory.Exists(output.Text)) Process.Start("explorer.exe", Engine.Quote(output.Text)); else MessageBox.Show("输出文件夹尚未创建。",Text); } catch(Exception ex) { MessageBox.Show(ex.Message,Text); }
            };
            state.SetBounds(24,517,752,23); state.Text="就绪 · 可将视频拖入列表"; Controls.Add(state);
            progress.SetBounds(24,547,752,10); Controls.Add(progress);
            log.SetBounds(24,573,752,65); log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical; log.BackColor=Color.White; Controls.Add(log);
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if(busy) { e.Cancel=true; engine.Cancelled=true; state.Text="正在取消，结束后可关闭窗口…"; } };
        }
        Label LabelAt(string text,int x,int y,int w,int h) { var c=new Label{Text=text}; c.SetBounds(x,y,w,h); Controls.Add(c); return c; }
        Button ButtonAt(string text,int x,int y,int w,int h) { var c=new Button{Text=text}; c.SetBounds(x,y,w,h); Controls.Add(c); return c; }
        void AddFiles(string[] paths) { if(busy) return; foreach(string p in paths) if(File.Exists(p) && !files.Items.Contains(p)) files.Items.Add(p); }
        void PickFiles() { using(var d=new OpenFileDialog()) { d.Multiselect=true; d.Title="选择一个或多个视频"; d.Filter="常见视频|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.wmv;*.flv;*.m4v;*.ts;*.mts;*.m2ts;*.mpg;*.mpeg;*.3gp;*.vob|所有文件|*.*"; if(d.ShowDialog()==DialogResult.OK) AddFiles(d.FileNames); } }
        void Ui(Action a) { if(!IsDisposed) BeginInvoke(a); }
        void Append(string text) { log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); }
        async Task StartBatch() {
            if(busy) return;
            if(files.Items.Count==0) { MessageBox.Show("请先添加视频。",Text); return; }
            string dest;
            try { dest=Path.GetFullPath(output.Text.Trim()); Directory.CreateDirectory(dest); }
            catch(Exception ex) { MessageBox.Show("输出位置不可用：" + ex.Message,Text); return; }
            var paths=new List<string>(); foreach(string p in files.Items) paths.Add(p);
            double seconds=(double)duration.Value; bool keep=tail.Checked;
            engine=new Engine();
            try { engine.CheckTools(); } catch(Exception ex) { MessageBox.Show(ex.Message,Text); return; }
            busy=true; output.Text=dest; start.Enabled=false; cancel.Enabled=true;
            foreach(Control c in editable) c.Enabled=false;
            progress.Value=0; log.Clear(); state.Text="准备处理…";
            int completed=0, failed=0;
            engine.Log=delegate(string s) { Ui(delegate { Append(s); }); };
            try {
                await Task.Run(delegate {
                    for(int i=0; i<paths.Count; i++) {
                        if(engine.Cancelled) break;
                        int index=i;
                        engine.Progress=delegate(int n) { Ui(delegate { progress.Value=Math.Min(100,(index*100+n)/paths.Count); }); };
                        Ui(delegate { state.Text="视频 " + (index+1) + "/" + paths.Count + " · " + Path.GetFileName(paths[index]); });
                        try { engine.Split(paths[i],dest,seconds,keep); completed++; }
                        catch(OperationCanceledException) { break; }
                        catch(Exception ex) { failed++; engine.Log("失败："+Path.GetFileName(paths[i])+"\r\n"+ex.Message); }
                    }
                });
                state.Text=(engine.Cancelled ? "已取消" : "处理结束") + " · 完成 " + completed + " 个视频 / 已保存 " + engine.TotalClips + " 个片段 · 失败 " + failed + " 个";
                Append(state.Text); if(!engine.Cancelled) progress.Value=100;
            } finally {
                busy=false; start.Enabled=true; cancel.Enabled=false;
                foreach(Control c in editable) c.Enabled=true;
            }
        }
    }
    static class Program {
        [STAThread] static int Main(string[] args) {
            if(args.Length==2 && args[0]=="--preview") {
                Application.EnableVisualStyles();
                using(var form=new MainForm()) using(var bitmap=new Bitmap(form.Width,form.Height)) {
                    form.ShowInTaskbar=false; form.Opacity=0; form.Show(); Application.DoEvents();
                    form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(args[1]); form.Close();
                } return 0;
            }
            if(args.Length>0) {
                if(args.Length<4 || args[0]!="--split") return 2;
                try { var e=new Engine(); e.Split(args[1],args[2],double.Parse(args[3],NumberStyles.Float,CultureInfo.InvariantCulture), !(args.Length>4 && args[4]=="--discard-tail")); return 0; }
                catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"命令行错误.txt"),ex.ToString(),Encoding.UTF8); return 1; }
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm()); return 0;
        }
    }
}
