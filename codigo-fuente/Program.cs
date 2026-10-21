using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PulsoIT {
    public sealed class Section {
        public string Id, Name, Description;
        public Section(string id,string name,string description){Id=id;Name=name;Description=description;}
        public override string ToString(){return Name;}
    }
    public sealed class Finding {
        public string Section, State, Title, Summary, Evidence, Next, Context="Equipo local",Code="",ScopeKey="local:"+Environment.MachineName,IncidentKey="";
        public Dictionary<string,object> Data=new Dictionary<string,object>();
        public DateTime Time;
        public Finding(string section,string state,string title,string summary,string evidence,string next){Section=section;State=state;Title=title;Summary=summary;Evidence=evidence;Next=next;Time=DateTime.Now;}
        public string MetaText(string key){object value;return Data!=null&&Data.TryGetValue(key,out value)&&value!=null?Convert.ToString(value,CultureInfo.InvariantCulture):"";}
        public Finding Copy(){var copy=(Finding)MemberwiseClone();copy.Data=Data==null?new Dictionary<string,object>():new Dictionary<string,object>(Data);return copy;}
        public string Comparable(){if(Data==null||Data.Count==0)return Evidence;return string.Join("|",Data.Where(p=>p.Key!="WindowStartUtc"&&p.Key!="WindowEndUtc"&&p.Key!="Milliseconds").OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"="+new JavaScriptSerializer().Serialize(p.Value)));}
        public static string Label(string state){switch(state){case "critical":return "INCIDENCIA";case "warning":return "REVISAR";case "good":return "SIN ALERTA";case "unknown":return "SIN VERIFICAR";default:return "DATO";}}
    }
    public static class Catalog {
        public static readonly string[] Symptoms={"El PC va lento","Se reinicia o se bloquea","No hay conexión","No imprime","Falla la VPN","Falla Escritorio remoto","Problemas de Intune"};
        public static readonly string[][] Profiles={
            new[]{"pc","discos","servicios","eventos","defender"},new[]{"eventos","discos","pc"},new[]{"red","vpn"},new[]{"impresoras","red","servicios"},new[]{"vpn","red","eventos"},new[]{"rdp","red","eventos"},new[]{"intune","servicios","eventos"}
        };
        public static readonly Section[] Sections={
            new Section("pc","PC y rendimiento","Windows, hardware, memoria, carga de CPU y procesos."),
            new Section("red","Red","Adaptadores, direcciones, rutas, DNS y prueba opcional de un destino."),
            new Section("rdp","Escritorio remoto","Configuración RDP local, puerto, NLA y conectividad TCP opcional."),
            new Section("eventos","Errores de Windows","Eventos agrupados en la ventana elegida o alrededor de la hora de la incidencia."),
            new Section("defender","Seguridad y Defender","Protección en tiempo real, modo de Defender y antigüedad de sus firmas."),
            new Section("discos","Discos","Espacio disponible y estado que comunica el proveedor de almacenamiento."),
            new Section("vpn","VPN","Perfiles VPN de Windows, adaptadores y rutas; sin conectar o desconectar."),
            new Section("software","Software instalado","Inventario parcial del registro y paquetes Appx del usuario actual."),
            new Section("servicios","Servicios","Estado de los servicios y automáticos detenidos que conviene revisar."),
            new Section("impresoras","Impresoras","Impresoras, puertos, trabajos pendientes y servicio de cola de impresión."),
            new Section("intune","Intune y MDM","Evidencias locales de gestión; no comprueba el cumplimiento en el portal."),
            new Section("servidores","Servidores","HealthCheck local o de un servidor indicado, con tu identidad y permisos actuales.")
        };
        public static readonly string[] Quick={"pc","red","discos","defender","eventos"};
        public static Section Get(string id){return Sections.First(s=>s.Id==id);}
    }
    public sealed class Engine {
        readonly Dictionary<string,object> scripts;
        public Engine(){
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("checks.json"))
            using(var reader=new StreamReader(stream,Encoding.UTF8)){
                scripts=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
            }
        }
        internal Engine(Dictionary<string,object> overrides){scripts=overrides;}
        public static string Quote(string value){return "'"+(value??"").Replace("'","''")+"'";}
        public static bool ValidTarget(string value){
            if(string.IsNullOrEmpty(value))return true;
            if(value.Length>253||value.Any(char.IsWhiteSpace))return false;
            System.Net.IPAddress ip;if(System.Net.IPAddress.TryParse(value,out ip))return true;
            return value.TrimEnd('.').Split('.').All(label=>Regex.IsMatch(label,@"^[a-zA-Z0-9_](?:[a-zA-Z0-9_\-]{0,61}[a-zA-Z0-9_])?$"));
        }
        public string Command(string id,string destination,int port,string server,int hours=24,IncidentContext context=null){
            if(!scripts.ContainsKey(id))throw new ArgumentException("Sección desconocida.");
            if(!ValidTarget(destination)||!ValidTarget(server)||port<1||port>65535||hours<1||hours>168)throw new ArgumentException("Destino, puerto o ventana de eventos no válidos.");
            Func<DateTime?,string> date=d=>d.HasValue?"[datetime]::Parse("+Quote(d.Value.ToString("o",CultureInfo.InvariantCulture))+",[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind).ToLocalTime()":"$null";
            return "$EmitirProgreso=$true;$Desde="+date(context==null?null:context.FromUtc)+";$Hasta="+date(context==null?null:context.ToUtc)+";$Horas="+hours+";$Destino="+Quote(destination)+";$Puerto="+port+";$Servidor="+Quote(server)+";\n"+(string)scripts[id];
        }
        public static List<Finding> Parse(string section,string json){
            var raw=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.DeserializeObject(json);
            var items=raw as object[];
            if(items==null) throw new FormatException("La consulta no devolvió una lista de resultados válida.");
            var result=new List<Finding>();
            foreach(var item in items){
                var d=item as Dictionary<string,object>;
                if(d==null || !d.ContainsKey("State") || !d.ContainsKey("Title")) throw new FormatException("Resultado incompleto.");
                Func<string,string> get=k=>d.ContainsKey(k)?Convert.ToString(d[k]):"";
                string state=get("State");
                if(!new[]{"good","warning","critical","unknown","info"}.Contains(state))state="unknown";
                object data;var f=new Finding(section,state,get("Title"),get("Summary"),get("Evidence"),get("Next")){Code=get("Code")};if(d.TryGetValue("Data",out data)&&data is Dictionary<string,object>)f.Data=(Dictionary<string,object>)data;result.Add(f);
            }
            if(result.Count==0)result.Add(new Finding(section,"unknown","Consulta sin resultados","El proveedor no devolvió observaciones.","","No se puede concluir el estado del equipo con esta respuesta."));
            return result;
        }
        public List<Finding> Run(string id,string destination,int port,string server,CancellationToken token,Action<Finding> onFinding=null,int hours=24,int timeoutSeconds=90,IncidentContext context=null){
            token.ThrowIfCancellationRequested();
            string command=Command(id,destination,port,server,hours,context);
            string binary=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
            if(!File.Exists(binary))throw new FileNotFoundException("Windows PowerShell no está disponible en este equipo.");
            var produced=new List<Finding>();var errors=new StringBuilder();object gate=new object();Exception protocolError=null;
            Action<Finding> publish=f=>{f.Context=id=="servidores"&&!string.IsNullOrEmpty(server)?"Servidor: "+server:"Equipo local: "+Environment.MachineName+(string.IsNullOrEmpty(destination)?"":" · prueba opcional: "+destination+":"+port);f.ScopeKey=id=="servidores"&&!string.IsNullOrEmpty(server)?"server:"+server.ToLowerInvariant():"local:"+Environment.MachineName;
                if(new[]{"network.tcp","network.dns","network.hosts","network.https","network.literal"}.Contains(f.Code))f.ScopeKey+="|target:"+destination.ToLowerInvariant()+":"+port;
                f.IncidentKey=context==null?"now":context.EvidenceKey;lock(gate)produced.Add(f);if(onFinding!=null)onFinding(f);};
            using(var process=new Process()){
                // El cuerpo viaja por stdin: evita el límite de longitud de la línea de comandos.
                string bootstrap="[Console]::InputEncoding=New-Object System.Text.UTF8Encoding($false);$body=[Console]::In.ReadToEnd(); & ([scriptblock]::Create($body))";
                process.StartInfo=new ProcessStartInfo(binary,"-NoLogo -NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(bootstrap))){
                    UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
                    RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
                };
                process.OutputDataReceived+=(s,e)=>{
                    if(string.IsNullOrWhiteSpace(e.Data))return;
                    try{if(e.Data.Length>8*1024*1024)throw new FormatException("Resultado demasiado grande.");string json=e.Data.Trim();foreach(var f in Parse(id,json.StartsWith("[")?json:"["+json+"]"))publish(f);}
                    catch(Exception ex){lock(gate){if(protocolError==null)protocolError=ex;}}
                };
                process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(gate){if(errors.Length<8192)errors.AppendLine(e.Data.Substring(0,Math.Min(8192-errors.Length,e.Data.Length)));}};
                process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
                var inputTask=Task.Run(()=>{byte[] body=Encoding.UTF8.GetBytes(command);var input=process.StandardInput.BaseStream;input.Write(body,0,body.Length);input.Flush();input.Close();});
                var timer=Stopwatch.StartNew();
                try {
                    while(!process.WaitForExit(150)){
                        token.ThrowIfCancellationRequested();
                        if(timer.Elapsed.TotalSeconds>timeoutSeconds)throw new TimeoutException("La sección ha superado los "+timeoutSeconds+" segundos. Se conservan las comprobaciones ya obtenidas.");
                    }
                    process.WaitForExit();token.ThrowIfCancellationRequested();
                    inputTask.GetAwaiter().GetResult();
                    if(process.ExitCode!=0)throw new InvalidOperationException("PowerShell no completó la consulta. Puede existir una restricción de la organización.\n"+errors.ToString().Trim());
                    if(protocolError!=null)throw new FormatException("Una respuesta no pudo interpretarse. Se conserva el resto de los resultados.",protocolError);
                    if(produced.Count==0)publish(new Finding(id,"unknown","Consulta sin resultados","El proveedor no devolvió observaciones.",errors.ToString(),"No se puede concluir el estado del equipo con una respuesta vacía."));
                    lock(gate)return produced.ToList();
                } finally {
                    try{if(!process.HasExited)process.Kill();if(process.WaitForExit(2000))process.WaitForExit();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
                    try{inputTask.Wait(2000);}catch(AggregateException){}
                }
            }
        }
    }
    public sealed class MainForm:Form {
        readonly Color Ink=Color.FromArgb(25,43,58), Muted=Color.FromArgb(87,104,116), Teal=Color.FromArgb(0,115,112), Back=Color.FromArgb(244,247,249);
        readonly Engine engine=new Engine();
        readonly Func<CancellationToken,ObservationSession> completeObserver;
        readonly List<Finding> all=new List<Finding>();
        readonly Dictionary<string,DateTime> completed=new Dictionary<string,DateTime>();
        readonly HashSet<string> partial=new HashSet<string>();
        readonly ListBox nav=new ListBox();readonly DataGridView grid=new DataGridView();readonly TextBox detail=new TextBox();
        readonly Label title=new Label(),subtitle=new Label(),status=new Label(),counts=new Label(),detailTitle=new Label();
        readonly Button quick=new Button(),full=new Button(),run=new Button(),cancel=new Button();
        readonly Button symptomRun=new Button();readonly ComboBox symptoms=new ComboBox(),timeWindow=new ComboBox();readonly Label windowLabel=new Label();
        readonly Button contextButton=new Button(),observe30=new Button(),observe60=new Button(),referenceButton=new Button(),compareButton=new Button();
        readonly ComboBox resultGroup=new ComboBox();readonly CheckBox testHttps=new CheckBox();readonly Label historicalWindow=new Label();
        readonly TabControl infoTabs=new TabControl();readonly TextBox interpretation=new TextBox(),comparison=new TextBox(),technical=new TextBox();
        IncidentContext incident=new IncidentContext();ObservationSession observed;MemoryReference reference;
        string[] requested=Catalog.Quick;DiagnosticAssessment assessment;
        readonly Font badgeFont=new Font("Segoe UI",8,FontStyle.Bold);
        readonly TextBox search=new TextBox(),target=new TextBox();readonly NumericUpDown port=new NumericUpDown();
        readonly Label targetLabel=new Label(),portLabel=new Label();readonly TableLayoutPanel inputPanel=new TableLayoutPanel();readonly FlowLayoutPanel symptomSelector=new FlowLayoutPanel();readonly ProgressBar progress=new ProgressBar();
        CancellationTokenSource cancellation;bool busy=false,closingRequested=false;string selected="overview";Finding shownFinding;
        public MainForm(Engine engineOverride=null,Func<CancellationToken,ObservationSession> observerOverride=null){
            if(engineOverride!=null)engine=engineOverride;completeObserver=observerOverride??(token=>ObservationRunner.Run(30,token,null));
            SuspendLayout();
            Text="Pulso IT · Diagnóstico portable";ClientSize=new Size(1220,810);MinimumSize=new Size(1050,650);
            using(var iconStream=Assembly.GetExecutingAssembly().GetManifestResourceStream("brand.ico"))
            using(var icon=new Icon(iconStream)){Icon=(Icon)icon.Clone();}
            AutoScaleDimensions=new SizeF(96F,96F);AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("Segoe UI",10);BackColor=Back;StartPosition=FormStartPosition.CenterScreen;
            var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,242));shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(shell);
            var left=new Panel{Dock=DockStyle.Fill,BackColor=Ink,Padding=new Padding(16)};shell.Controls.Add(left,0,0);
            var brand=new Panel{Dock=DockStyle.Top,Height=60};
            var logo=new PictureBox{Bounds=new Rectangle(0,3,54,54),SizeMode=PictureBoxSizeMode.Zoom};
            using(var logoStream=Assembly.GetExecutingAssembly().GetManifestResourceStream("brand.png"))
            using(var image=Image.FromStream(logoStream)){logo.Image=new Bitmap(image);}
            var wordmark=new Label{Text="PULSO IT",ForeColor=Color.White,Font=new Font("Segoe UI",19,FontStyle.Bold),Bounds=new Rectangle(62,0,148,60),TextAlign=ContentAlignment.MiddleLeft};
            brand.Controls.Add(logo);brand.Controls.Add(wordmark);
            FormClosed+=(s,e)=>{if(logo.Image!=null)logo.Image.Dispose();};
            var note=new Label{Text="Diagnóstico portable\nv2.1.0 · diagnóstico explicado",ForeColor=Color.FromArgb(172,203,210),Dock=DockStyle.Top,Height=61};
            var footer=new Label{Text="Permisos de tu cuenta\nSin instalación · Sin informes",ForeColor=Color.FromArgb(172,203,210),Dock=DockStyle.Bottom,Height=58};
            nav.Dock=DockStyle.Fill;nav.BackColor=Ink;nav.ForeColor=Color.White;nav.BorderStyle=BorderStyle.None;nav.ItemHeight=37;nav.IntegralHeight=false;nav.DrawMode=DrawMode.OwnerDrawFixed;
            nav.Items.Add("Resumen");foreach(var s in Catalog.Sections)nav.Items.Add(s.Name);
            nav.DrawItem+=DrawNavigation;nav.SelectedIndexChanged+=(s,e)=>SelectSection();
            left.Controls.Add(nav);left.Controls.Add(note);left.Controls.Add(brand);left.Controls.Add(footer);
            var main=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,18,24,12),ColumnCount=1,RowCount=9};shell.Controls.Add(main,1,0);
            main.RowStyles.Add(new RowStyle(SizeType.Absolute,79));main.RowStyles.Add(new RowStyle(SizeType.Absolute,50));main.RowStyles.Add(new RowStyle(SizeType.AutoSize));main.RowStyles.Add(new RowStyle(SizeType.AutoSize));main.RowStyles.Add(new RowStyle(SizeType.Absolute,42));main.RowStyles.Add(new RowStyle(SizeType.Percent,40));main.RowStyles.Add(new RowStyle(SizeType.Absolute,28));main.RowStyles.Add(new RowStyle(SizeType.Percent,60));main.RowStyles.Add(new RowStyle(SizeType.Absolute,39));
            var heading=new Panel{Dock=DockStyle.Fill};title.SetBounds(0,0,850,37);title.Font=new Font("Segoe UI",23,FontStyle.Bold);title.ForeColor=Ink;title.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
            subtitle.SetBounds(1,43,850,32);subtitle.ForeColor=Muted;subtitle.Anchor=title.Anchor;heading.Controls.Add(title);heading.Controls.Add(subtitle);main.Controls.Add(heading,0,0);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,3,0,0)};
            SetupButton(quick,"Diagnóstico rápido",184,true);SetupButton(full,"Diagnóstico completo",205,false);SetupButton(run,"Diagnosticar sección",185,false);SetupButton(cancel,"Detener",95,false);
            actions.Controls.AddRange(new Control[]{quick,full,run,cancel});main.Controls.Add(actions,0,1);cancel.Enabled=false;
            quick.Click+=(s,e)=>Start(Catalog.Quick);full.Click+=(s,e)=>Start(Catalog.Sections.Select(x=>x.Id).ToArray(),true);run.Click+=(s,e)=>{if(selected!="overview")Start(new[]{selected});};cancel.Click+=(s,e)=>{if(cancellation!=null){cancellation.Cancel();cancel.Enabled=false;status.Text="Deteniendo la consulta en curso…";}};
            inputPanel.Dock=DockStyle.Fill;inputPanel.AutoSize=true;inputPanel.AutoSizeMode=AutoSizeMode.GrowAndShrink;inputPanel.BackColor=Color.FromArgb(231,240,242);inputPanel.Padding=new Padding(12,8,12,8);
            inputPanel.ColumnCount=3;inputPanel.RowCount=2;inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,106));inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,200));inputPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));inputPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            targetLabel.AutoSize=true;targetLabel.Dock=DockStyle.Fill;targetLabel.ForeColor=Muted;targetLabel.Margin=new Padding(0,0,12,6);
            target.Dock=DockStyle.Fill;target.Margin=new Padding(0,3,12,3);target.MaxLength=253;
            portLabel.Text="Puerto TCP";portLabel.AutoSize=true;portLabel.Dock=DockStyle.Fill;portLabel.ForeColor=Muted;portLabel.Margin=new Padding(0,0,12,6);
            port.Dock=DockStyle.Top;port.Margin=new Padding(0,3,12,3);port.Minimum=1;port.Maximum=65535;port.Value=3389;
            inputPanel.Controls.Add(targetLabel,0,0);inputPanel.Controls.Add(portLabel,1,0);inputPanel.Controls.Add(port,1,1);main.Controls.Add(inputPanel,0,2);
            symptoms.DropDownStyle=ComboBoxStyle.DropDownList;symptoms.Items.AddRange(Catalog.Symptoms);symptoms.SelectedIndex=0;symptoms.Width=270;symptoms.Margin=new Padding(0,4,10,4);
            SetupButton(symptomRun,"Analizar síntoma",163,true);symptomRun.AutoSize=true;symptomRun.AutoSizeMode=AutoSizeMode.GrowAndShrink;symptomRun.MinimumSize=new Size(163,34);symptomRun.Margin=new Padding(0,0,8,0);symptomRun.Click+=(s,e)=>{if(ConfigureIncident(symptoms.SelectedIndex))Start(Catalog.Profiles[symptoms.SelectedIndex]);};
            symptomSelector.Dock=DockStyle.Fill;symptomSelector.AutoSize=true;symptomSelector.AutoSizeMode=AutoSizeMode.GrowAndShrink;symptomSelector.WrapContents=true;symptomSelector.Margin=Padding.Empty;symptomSelector.Controls.AddRange(new Control[]{symptoms,symptomRun});
            windowLabel.Text="Ventana de eventos";windowLabel.AutoSize=true;windowLabel.ForeColor=Muted;windowLabel.Dock=DockStyle.Fill;windowLabel.Margin=new Padding(6,0,0,6);
            timeWindow.DropDownStyle=ComboBoxStyle.DropDownList;timeWindow.Items.AddRange(new object[]{"Últimas 2 horas","Últimas 24 horas","Últimos 3 días","Últimos 7 días"});timeWindow.SelectedIndex=1;timeWindow.Dock=DockStyle.Top;timeWindow.Margin=new Padding(6,3,0,3);
            inputPanel.Controls.Add(windowLabel,2,0);inputPanel.Controls.Add(timeWindow,2,1);
            historicalWindow.AutoSize=true;historicalWindow.Dock=DockStyle.Top;historicalWindow.Margin=new Padding(6,3,0,3);historicalWindow.ForeColor=Muted;
            testHttps.Text="Validar HTTPS (HEAD /, sin credenciales)";testHttps.AutoSize=true;testHttps.Margin=new Padding(0,5,0,3);testHttps.Checked=false;inputPanel.Controls.Add(testHttps,0,2);inputPanel.SetColumnSpan(testHttps,3);inputPanel.RowCount=3;inputPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var tools=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Fill,WrapContents=true,Margin=new Padding(3,5,3,5)};
            SetupButton(contextButton,"Contexto de incidencia",175,false);SetupButton(observe30,"Observar 30 s",130,false);SetupButton(observe60,"Observar 60 s",130,false);SetupButton(referenceButton,"Guardar referencia",160,false);SetupButton(compareButton,"Comparar",105,false);
            tools.Controls.AddRange(new Control[]{contextButton,observe30,observe60,referenceButton,compareButton});main.Controls.Add(tools,0,3);
            contextButton.Click+=(s,e)=>ConfigureIncident();observe30.Click+=(s,e)=>Observe(30);observe60.Click+=(s,e)=>Observe(60);referenceButton.Click+=(s,e)=>SaveReference();compareButton.Click+=(s,e)=>CompareReference();
            var filter=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1};filter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,220));
            filter.RowStyles.Add(new RowStyle(SizeType.Percent,100));counts.Dock=DockStyle.Fill;counts.ForeColor=Muted;counts.TextAlign=ContentAlignment.MiddleLeft;
            resultGroup.DropDownStyle=ComboBoxStyle.DropDownList;resultGroup.Items.AddRange(new object[]{"Todos","Para revisar","Pendientes","Contexto"});resultGroup.Dock=DockStyle.Fill;resultGroup.Margin=new Padding(0,6,8,4);resultGroup.SelectedIndex=0;resultGroup.SelectedIndexChanged+=(s,e)=>RefreshGrid();search.Dock=DockStyle.Fill;search.Margin=new Padding(0,6,0,4);search.TextChanged+=(s,e)=>RefreshGrid();
            var searchPanel=new Panel{Dock=DockStyle.Fill};var searchLabel=new Label{Text="Buscar",Dock=DockStyle.Left,Width=52,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Muted};searchPanel.Controls.Add(search);searchPanel.Controls.Add(searchLabel);
            filter.Controls.Add(counts,0,0);filter.Controls.Add(resultGroup,1,0);filter.Controls.Add(searchPanel,2,0);main.Controls.Add(filter,0,4);
            grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.AllowUserToResizeRows=false;grid.RowHeadersVisible=false;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.None;grid.AutoGenerateColumns=false;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersHeight=36;grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(225,233,237);grid.ColumnHeadersDefaultCellStyle.ForeColor=Ink;grid.ColumnHeadersDefaultCellStyle.Font=new Font("Segoe UI",9,FontStyle.Bold);grid.DefaultCellStyle.Font=new Font("Segoe UI",9);grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.DefaultCellStyle.Padding=new Padding(5,7,5,7);grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(215,237,236);grid.DefaultCellStyle.SelectionForeColor=Ink;grid.GridColor=Color.FromArgb(235,240,242);
            grid.Columns.Add(new DataGridViewTextBoxColumn{Name="State",HeaderText="Estado",Width=124,SortMode=DataGridViewColumnSortMode.NotSortable});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Section",HeaderText="Sección",Width=152,SortMode=DataGridViewColumnSortMode.NotSortable});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Result",HeaderText="Comprobación y resultado",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,SortMode=DataGridViewColumnSortMode.NotSortable});grid.SelectionChanged+=(s,e)=>ShowDetail();grid.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0)infoTabs.SelectedIndex=0;};main.Controls.Add(grid,0,5);
            detailTitle.Text="Conclusiones y explicación · datos técnicos disponibles por separado";detailTitle.Dock=DockStyle.Fill;detailTitle.TextAlign=ContentAlignment.MiddleLeft;detailTitle.Font=new Font("Segoe UI",10,FontStyle.Bold);detailTitle.ForeColor=Ink;main.Controls.Add(detailTitle,0,6);
            detail.Dock=DockStyle.Fill;detail.ReadOnly=true;detail.Multiline=true;detail.MaxLength=0;detail.ScrollBars=ScrollBars.Vertical;detail.BackColor=Color.White;detail.ForeColor=Ink;detail.BorderStyle=BorderStyle.None;detail.Font=new Font("Segoe UI",10);detail.WordWrap=true;
            infoTabs.Dock=DockStyle.Fill;var tabDetail=new TabPage("Qué significa");tabDetail.Controls.Add(detail);infoTabs.TabPages.Add(tabDetail);AddInfoTab("Diagnóstico",interpretation);AddInfoTab("Antes / después",comparison);AddInfoTab("Datos técnicos",technical);infoTabs.SelectedIndex=1;main.Controls.Add(infoTabs,0,7);
            var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));status.Dock=DockStyle.Fill;status.ForeColor=Muted;status.TextAlign=ContentAlignment.MiddleLeft;progress.Dock=DockStyle.Fill;progress.Margin=new Padding(8,12,0,10);bottom.Controls.Add(status,0,0);bottom.Controls.Add(progress,1,0);main.Controls.Add(bottom,0,8);
            FormClosing+=(s,e)=>{if(busy){e.Cancel=true;closingRequested=true;if(cancellation!=null)cancellation.Cancel();status.Text="Deteniendo las consultas antes de cerrar…";}};FormClosed+=(s,e)=>{badgeFont.Dispose();};nav.SelectedIndex=0;
            Shown+=(s,e)=>{var area=Screen.GetWorkingArea(this);if(Height>area.Height){Height=area.Height;Top=area.Top;}if(Width>area.Width){Width=area.Width;Left=area.Left;}};
            ResumeLayout(true);
        }
        void SetupButton(Button b,string text,int width,bool primary){b.Text=text;b.Size=new Size(width,36);b.Margin=new Padding(0,0,9,0);b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderColor=primary?Teal:Color.FromArgb(196,211,216);b.BackColor=primary?Teal:Color.White;b.ForeColor=primary?Color.White:Ink;b.Cursor=Cursors.Hand;b.Font=new Font("Segoe UI",9,FontStyle.Bold);}
        void AddInfoTab(string titleText,TextBox box){box.Multiline=true;box.ReadOnly=true;box.MaxLength=0;box.ScrollBars=ScrollBars.Vertical;box.Dock=DockStyle.Fill;box.BorderStyle=BorderStyle.None;box.BackColor=Color.White;box.Font=new Font("Segoe UI",10);var tab=new TabPage(titleText);tab.Padding=new Padding(6);tab.Controls.Add(box);infoTabs.TabPages.Add(tab);}
        bool ConfigureIncident(int symptom=-1){if(busy)return false;using(var dialog=new IncidentDialog(incident,symptom)){if(dialog.ShowDialog(this)!=DialogResult.OK)return false;incident=dialog.Value;int chosen=Array.IndexOf(Catalog.Symptoms,incident.Symptom);if(chosen>=0)symptoms.SelectedIndex=chosen;}UpdateWindowInput();RefreshGrid();return true;}
        void UpdateWindowInput(){inputPanel.SuspendLayout();inputPanel.Controls.Remove(timeWindow);inputPanel.Controls.Remove(historicalWindow);windowLabel.Text=incident.IsNow?"Ventana de eventos":"Hora de incidencia";if(incident.IsNow)inputPanel.Controls.Add(timeWindow,2,1);else{historicalWindow.Text=incident.IncidentLocal.ToString("g")+"\r\n± "+incident.MarginMinutes+" min";inputPanel.Controls.Add(historicalWindow,2,1);}inputPanel.ResumeLayout(true);}
        void Observe(int seconds){if(busy)return;using(var dialog=new ObservationForm(seconds)){dialog.ShowDialog(this);if(dialog.Session!=null){observed=dialog.Session;AddObservation();RefreshGrid();infoTabs.SelectedIndex=1;}}}
        void AddObservation(){if(observed==null)return;all.RemoveAll(f=>f.Code=="observation.summary");all.Add(new Finding("pc",observed.Samples.Any(s=>s.Get("cpu").HasValue||s.Get("commit").HasValue||s.Get("readMs").HasValue)?"info":"unknown","Observación de rendimiento",observed.Samples.Count+" muestras · "+(observed.Complete?"terminada":"parcial"),observed.ShortSummary(),"Consulta la pestaña Diagnóstico. Las mediciones actuales no reconstruyen una incidencia pasada."){Code="observation.summary",Time=observed.Ended,IncidentKey="now"});}
        void SaveReference(){if(busy)return;if(all.Count==0&&observed==null){status.Text="Ejecuta una consulta antes de guardar una referencia.";return;}reference=MemoryReference.Capture(all,observed);referenceButton.Text="Actualizar referencia";comparison.Text="Referencia guardada solo en memoria a las "+reference.Captured.ToString("T")+".\r\nVuelve a consultar después del cambio que quieras contrastar y pulsa Comparar. Se elimina al cerrar la app.";infoTabs.SelectedIndex=2;status.Text="Referencia en memoria preparada; no se ha escrito ningún archivo.";}
        void CompareReference(){if(reference==null){status.Text="Primero guarda una referencia en memoria.";return;}comparison.Text=reference.Compare(all,observed);infoTabs.SelectedIndex=2;}
        void DrawNavigation(object sender,DrawItemEventArgs e){
            if(e.Index<0)return;bool sel=(e.State&DrawItemState.Selected)!=0;
            using(var bg=new SolidBrush(sel?Color.FromArgb(36,79,91):Ink))e.Graphics.FillRectangle(bg,e.Bounds);
            if(sel)using(var accent=new SolidBrush(Color.FromArgb(69,203,178)))e.Graphics.FillRectangle(accent,new Rectangle(e.Bounds.Left,e.Bounds.Top,3,e.Bounds.Height));
            using(var font=new Font("Segoe UI",10,sel?FontStyle.Bold:FontStyle.Regular))TextRenderer.DrawText(e.Graphics,nav.Items[e.Index].ToString(),font,new Rectangle(e.Bounds.Left+12,e.Bounds.Top,e.Bounds.Width-12,e.Bounds.Height),Color.White,TextFormatFlags.VerticalCenter|TextFormatFlags.Left);
        }
        void SelectSection(){
            selected=nav.SelectedIndex<=0?"overview":Catalog.Sections[nav.SelectedIndex-1].Id;
            title.Text=selected=="overview"?"¿Qué le ocurre a este PC?":Catalog.Get(selected).Name;
            subtitle.Text=selected=="overview"?"Pulsa Diagnóstico completo: doce secciones y 30 s de observación, sin modificar el equipo.":Catalog.Get(selected).Description;
            run.Enabled=!busy&&selected!="overview";
            bool server=selected=="servidores",network=selected=="red"||selected=="rdp";
            symptoms.Visible=symptomRun.Visible=selected=="overview";symptomRun.Enabled=!busy;
            inputPanel.SuspendLayout();inputPanel.Visible=true;target.Visible=server||network;
            inputPanel.Controls.Remove(target);inputPanel.Controls.Remove(symptomSelector);
            inputPanel.SetColumnSpan(targetLabel,network?1:2);
            if(selected=="overview"){inputPanel.Controls.Add(symptomSelector,0,1);inputPanel.SetColumnSpan(symptomSelector,2);}
            else if(server||network){inputPanel.Controls.Add(target,0,1);inputPanel.SetColumnSpan(target,server?2:1);}
            targetLabel.Text=server?"Servidor opcional · vacío = este equipo":network?"Destino opcional · vacío = solo configuración local":selected=="overview"?"Elige un síntoma o inicia el diagnóstico completo.\nLa pestaña Diagnóstico reúne las conclusiones.":"Consultas con los permisos de tu cuenta.\nLos resultados se muestran aquí, sin generar archivos.";
            port.Visible=network;portLabel.Visible=network;
            testHttps.Visible=selected=="red";if(selected!="red")testHttps.Checked=false;
            target.Text="";port.Value=selected=="red"?443:3389;
            inputPanel.ResumeLayout(true);
            LayoutInputs();
            RefreshGrid();
        }
        void LayoutInputs(){
            inputPanel.PerformLayout();
        }
        void RefreshGrid(){
            int scroll=grid.FirstDisplayedScrollingRowIndex;
            Finding previous=grid.CurrentRow==null?null:grid.CurrentRow.Tag as Finding;
            IEnumerable<Finding> items=all.Where(x=>selected=="overview"||x.Section==selected);
            if(resultGroup.SelectedIndex==1)items=items.Where(x=>x.State=="warning"||x.State=="critical");
            else if(resultGroup.SelectedIndex==2)items=items.Where(x=>x.State=="unknown");
            else if(resultGroup.SelectedIndex==3)items=items.Where(x=>x.State=="good"||x.State=="info");
            string term=search.Text.Trim();if(term.Length>0)items=items.Where(x=>(x.Title+" "+x.Summary+" "+x.Evidence+" "+Catalog.Get(x.Section).Name).IndexOf(term,StringComparison.CurrentCultureIgnoreCase)>=0);
            items=items.OrderBy(x=>Rank(x.State)).ThenBy(x=>Array.FindIndex(Catalog.Sections,s=>s.Id==x.Section));
            grid.Rows.Clear();
            foreach(var f in items){int i=grid.Rows.Add(Finding.Label(f.State),Catalog.Get(f.Section).Name,f.Title+Environment.NewLine+f.Summary);var row=grid.Rows[i];row.Tag=f;row.Cells[0].Style.ForeColor=StateColor(f.State);row.Cells[0].Style.Font=badgeFont;if(f==previous)grid.CurrentCell=row.Cells[0];}
            if(scroll>=0&&scroll<grid.Rows.Count)grid.FirstDisplayedScrollingRowIndex=scroll;
            var relevant=all.Where(x=>selected=="overview"||x.Section==selected).ToList();
            int issues=relevant.Count(x=>x.State=="critical"||x.State=="warning"),unknown=relevant.Count(x=>x.State=="unknown");
            counts.Text=issues+" para revisar · "+unknown+" pendientes · "+grid.Rows.Count+"/"+relevant.Count+" visibles";
            if(!busy)status.Text=selected=="overview"?completed.Count+" de 12 secciones consultadas · "+partial.Count+" parciales · "+Environment.MachineName:(partial.Contains(selected)?"Consulta parcial: se conservan los datos obtenidos.":completed.ContainsKey(selected)?"Última consulta: "+completed[selected].ToString("HH:mm:ss"):"Esta sección todavía no se ha revisado.");
            ShowDetail();
            assessment=Assessment.Build(all,observed,incident,DateTime.Now,completed,requested,busy,partial);
            if(interpretation.Text!=assessment.Report)interpretation.Text=assessment.Report;
            if(selected=="overview"&&all.Count>0){title.Text=assessment.State=="running"?"Analizando el equipo…":assessment.Cases.Count>0?"Hay hallazgos para revisar":assessment.State=="good"?"Sin incidencias detectadas":"Diagnóstico y cobertura";subtitle.Text=assessment.Headline;subtitle.ForeColor=StateColor(assessment.State);}
            detailTitle.Text=assessment.Reviewed>0?assessment.Cases.Count+" grupos de hallazgos · "+assessment.Complete+" secciones completas · doble clic en un resultado para entenderlo":"Conclusiones y explicación · datos técnicos disponibles por separado";
        }
        static int Rank(string s){return s=="critical"?0:s=="warning"?1:s=="unknown"?2:s=="good"?3:4;}
        Color StateColor(string s){return s=="critical"?Color.FromArgb(174,42,52):s=="warning"?Color.FromArgb(144,85,0):s=="good"?Color.FromArgb(0,113,83):s=="unknown"?Color.FromArgb(114,77,142):Muted;}
        void ShowDetail(){
            Finding f=grid.CurrentRow==null?null:grid.CurrentRow.Tag as Finding;
            if(f!=null&&f==shownFinding)return;shownFinding=f;
            if(f==null){technical.Text="Selecciona una comprobación para consultar sus datos originales.";detail.Text=all.Count==0?"Pulsa «Diagnóstico rápido» para revisar PC, red, discos, Defender y errores de Windows.\n\nPuedes abrir cualquiera de las 12 secciones y diagnosticarla por separado. La app no modifica la configuración ni crea informes.\n\nLas consultas que necesiten permisos adicionales se mostrarán como «Sin verificar». Un PC que no inicia Windows requiere otro tipo de herramienta.":"No hay resultados visibles con este filtro. Cambia de sección, cambia el grupo de resultados o borra la búsqueda.";detail.Text=detail.Text.Replace("\r\n","\n").Replace("\n",Environment.NewLine);return;}
            detail.Text=PlainLanguage.Detail(f);
            technical.Text=f.Title+Environment.NewLine+f.Context+Environment.NewLine+"Consultado: "+f.Time.ToString("g")+Environment.NewLine+"Código interno: "+f.Code+Environment.NewLine+Environment.NewLine+f.Summary+Environment.NewLine+Environment.NewLine+"DATOS ORIGINALES"+Environment.NewLine+(string.IsNullOrWhiteSpace(f.Evidence)?"Sin datos adicionales.":f.Evidence)+Environment.NewLine+Environment.NewLine+"ORIENTACIÓN DE LA CONSULTA"+Environment.NewLine+f.Next;
            detail.SelectionStart=0;detail.SelectionLength=0;detail.ScrollToCaret();
        }
        async void Start(string[] ids,bool completeDiagnosis=false){
            if(busy)return;
            if(ids.Length>1)nav.SelectedIndex=0;
            string destination="",server="";int tcpPort=(int)port.Value;
            if(ids.Length==1&&(ids[0]=="red"||ids[0]=="rdp"||ids[0]=="servidores")){
                string value=target.Text.Trim();
                if(!Engine.ValidTarget(value)){MessageBox.Show(this,"Escribe un nombre de equipo o una IP válidos, sin URL ni espacios.","Revisa el destino",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
                if(ids[0]=="servidores")server=value;else destination=value;
            }
            bool https=ids.Length==1&&ids[0]=="red"&&testHttps.Checked&&!string.IsNullOrEmpty(destination);var context=incident.Copy();
            requested=ids.ToArray();busy=true;cancellation=new CancellationTokenSource();var token=cancellation.Token;int hours=new[]{2,24,72,168}[timeWindow.SelectedIndex];
            quick.Enabled=false;full.Enabled=false;run.Enabled=false;symptomRun.Enabled=false;timeWindow.Enabled=false;symptoms.Enabled=false;cancel.Enabled=true;target.Enabled=false;port.Enabled=false;
            contextButton.Enabled=observe30.Enabled=observe60.Enabled=referenceButton.Enabled=compareButton.Enabled=testHttps.Enabled=false;
            var pending=new ConcurrentQueue<Finding>();
            Action drain=()=>{Finding item;bool changed=false;while(pending.TryDequeue(out item)){all.Add(item);changed=true;}if(changed&&!IsDisposed&&!Disposing)RefreshGrid();};
            var updates=new System.Windows.Forms.Timer{Interval=250};updates.Tick+=(s,e)=>drain();updates.Start();string current=null;
            progress.Style=ProgressBarStyle.Continuous;progress.Minimum=0;progress.Maximum=ids.Length;progress.Value=0;
            all.RemoveAll(x=>ids.Contains(x.Section));foreach(var id in ids){completed.Remove(id);partial.Remove(id);}RefreshGrid();
            try {
                foreach(var id in ids){
                    current=id;
                    token.ThrowIfCancellationRequested();status.Text="Consultando "+Catalog.Get(id).Name+"… ("+(progress.Value+1)+"/"+ids.Length+")";
                    try{await Task.Run(()=>engine.Run(id,destination,tcpPort,server,token,f=>pending.Enqueue(f),hours,90,context),token);if(https&&id=="red"){status.Text="Validando HTTPS sin credenciales…";var web=await Task.Run(()=>HttpsProbe.Run(destination,tcpPort,token),token);web.IncidentKey=context.EvidenceKey;pending.Enqueue(web);}}
                    catch(OperationCanceledException){throw;}
                    catch(Exception ex){partial.Add(id);pending.Enqueue(new Finding(id,"unknown","Consulta parcial",ex is TimeoutException?"El proveedor no respondió a tiempo.":"Una parte de la sección no pudo completarse.",ex.Message,"Se conservan las comprobaciones anteriores. Puedes repetir esta sección; un error o un timeout no demuestran una avería del equipo."){IncidentKey=context.EvidenceKey,ScopeKey=string.IsNullOrEmpty(server)?"local:"+Environment.MachineName:"server:"+server.ToLowerInvariant()});}
                    if(IsDisposed||Disposing)return;
                    drain();if(all.Any(x=>x.Section==id&&x.State=="unknown"))partial.Add(id);completed[id]=DateTime.Now;progress.Value++;RefreshGrid();current=null;
                }
                if(completeDiagnosis&&context.IsNow){
                    status.Text="Observando rendimiento durante 30 s… Puedes usar el PC para reproducir el fallo.";progress.Style=ProgressBarStyle.Marquee;
                    try{observed=await Task.Run(()=>completeObserver(token));AddObservation();token.ThrowIfCancellationRequested();}
                    catch(OperationCanceledException){throw;}
                    catch(Exception ex){all.RemoveAll(f=>f.Code=="observation.summary");all.Add(new Finding("pc","unknown","Observación de rendimiento","No se pudo completar el intervalo de rendimiento.",ex.Message,"Las consultas anteriores se conservan. Repite la observación para verificar las métricas disponibles."){Code="observation.summary",IncidentKey=context.EvidenceKey});}
                }
                status.Text="Diagnóstico terminado. Lee la conclusión y los siguientes pasos.";
                infoTabs.SelectedIndex=1;
            } catch(OperationCanceledException){if(current!=null){partial.Add(current);pending.Enqueue(new Finding(current,"unknown","Consulta detenida","Esta sección quedó incompleta por cancelación.","Se conservan los resultados que ya habían llegado.","Repite la sección para completar las comprobaciones pendientes."){IncidentKey=context.EvidenceKey,ScopeKey=string.IsNullOrEmpty(server)?"local:"+Environment.MachineName:"server:"+server.ToLowerInvariant()});}drain();if(!IsDisposed)status.Text="Consulta detenida. Se conservan los resultados obtenidos, incluso de la sección en curso.";}
            finally {
                updates.Stop();updates.Dispose();drain();busy=false;progress.Style=ProgressBarStyle.Continuous;if(cancellation!=null){cancellation.Dispose();cancellation=null;}
                if(!IsDisposed&&!Disposing){RefreshGrid();quick.Enabled=true;full.Enabled=true;run.Enabled=selected!="overview";symptomRun.Enabled=true;symptoms.Enabled=true;timeWindow.Enabled=true;cancel.Enabled=false;target.Enabled=true;port.Enabled=true;if(closingRequested)Close();}
                if(!IsDisposed&&!Disposing)contextButton.Enabled=observe30.Enabled=observe60.Enabled=referenceButton.Enabled=compareButton.Enabled=testHttps.Enabled=true;
            }
        }
        public void LoadPreview(){
            all.Add(new Finding("discos","critical","Espacio C:","3,8% libre · 9,1 GB de 238 GB","DeviceID: C:\nVolumeName: Windows\nFreeSpace: 9,1 GB\nSize: 238 GB","El poco espacio puede afectar a Windows y sus aplicaciones. Revisa el uso del disco antes de eliminar archivos."){Code="disk.space",Data=new Dictionary<string,object>{{"Identity","C:"},{"FreePercent",3.8}}});
            all.Add(new Finding("pc","warning","Memoria RAM","93% en uso · 0,6 GB libres","Memoria total: 8 GB\nDisponible: 0,6 GB","Comprueba los procesos que más memoria consumen mientras se produce la lentitud."){Code="memory.snapshot"});
            all.Add(new Finding("intune","unknown","Tareas de gestión","Tu cuenta no tiene acceso a esta consulta.","Acceso denegado al consultar EnterpriseMgmt.","Pide al responsable que revise esta comprobación con el permiso delegado necesario."));
            all.Add(new Finding("red","good","Adaptadores de red","1 adaptador activo de 2","Ethernet: Up · 1 Gbps\nWi-Fi: Disconnected","El enlace activo no garantiza acceso a un servicio concreto."));
            all.Add(new Finding("defender","good","Protección de Defender","Antivirus: True · tiempo real: True","AMRunningMode: Normal","No se han iniciado análisis ni actualizaciones."));
            all.Add(new Finding("eventos","info","Errores recientes","Revisa la coincidencia con la hora de la incidencia.","Ejemplo de datos simulados.","Los eventos no identifican automáticamente la causa."));
            foreach(var id in Catalog.Quick)completed[id]=DateTime.Now;
            RefreshGrid();status.Text="VISTA DE DEMOSTRACIÓN · Datos simulados, no corresponden a este PC.";
        }
        internal bool DiagnosticBusy {get{return busy;}}
        internal DiagnosticAssessment LastAssessment {get{return assessment;}}
        internal void RunCompleteForTest(){Start(Catalog.Sections.Select(x=>x.Id).ToArray(),true);}
        internal void CancelForTest(){if(cancellation!=null)cancellation.Cancel();}
        internal bool VerifyView(){
            LoadPreview();if(grid.Rows.Count!=6||!detail.Text.Contains("QUÉ REVISAR")||!interpretation.Text.Contains("POR DÓNDE EMPEZAR")||technical.Text.Length==0)return false;
            resultGroup.SelectedIndex=1;if(grid.Rows.Count!=2)return false;
            search.Text="RAM";if(grid.Rows.Count!=1)return false;
            search.Text="";resultGroup.SelectedIndex=2;if(grid.Rows.Count!=1)return false;resultGroup.SelectedIndex=0;nav.SelectedIndex=10;
            if(selected!="impresoras"||grid.Rows.Count!=0)return false;
            nav.SelectedIndex=12;if(selected!="servidores"||!target.Visible||port.Visible)return false;
            Size original=Size;Size=MinimumSize;PerformLayout();Application.DoEvents();nav.SelectedIndex=2;LayoutInputs();
            if(target.Right+8>port.Left||port.Right+8>timeWindow.Left||timeWindow.Right>inputPanel.ClientSize.Width)return false;
            nav.SelectedIndex=0;LayoutInputs();if(!symptoms.Visible||symptomRun.Right+8>timeWindow.Left||timeWindow.Items.Count!=4)return false;
            Size=original;PerformLayout();
            return true;
        }
        internal bool VerifyInputGeometry(){
            foreach(int index in new[]{0,2,3,11,12}){
                nav.SelectedIndex=index;PerformLayout();inputPanel.PerformLayout();Application.DoEvents();
                var visible=inputPanel.Controls.Cast<Control>().Where(c=>c.Visible).ToArray();
                foreach(var c in visible)if(!inputPanel.ClientRectangle.Contains(c.Bounds))return LayoutFailure("Fuera del panel: "+index+" "+c.GetType().Name+" "+c.Bounds+" panel "+inputPanel.ClientRectangle);
                for(int i=0;i<visible.Length;i++)for(int j=i+1;j<visible.Length;j++)if(visible[i].Bounds.IntersectsWith(visible[j].Bounds))return LayoutFailure("Solapamiento: "+index+" "+visible[i].GetType().Name+" / "+visible[j].GetType().Name);
                if(targetLabel.Height<targetLabel.GetPreferredSize(new Size(targetLabel.Width,0)).Height)return LayoutFailure("Etiqueta recortada: "+index+" actual "+targetLabel.Size+" preferido "+targetLabel.GetPreferredSize(new Size(targetLabel.Width,0)));
                if(index==0){
                    foreach(Control c in symptomSelector.Controls)if(!symptomSelector.ClientRectangle.Contains(c.Bounds))return LayoutFailure("Selector recortado: "+c.GetType().Name+" "+c.Bounds+" panel "+symptomSelector.ClientRectangle);
                    // El combo nativo puede diferir dos píxeles de PreferredHeight según el tema.
                    if(symptoms.Height<symptoms.Font.Height+2||symptomRun.Height<symptomRun.PreferredSize.Height)return LayoutFailure("Altura insuficiente: combo "+symptoms.Height+", fuente "+symptoms.Font.Height+", boton "+symptomRun.Height+" / "+symptomRun.PreferredSize.Height);
                    if(symptoms.Bounds.IntersectsWith(symptomRun.Bounds))return LayoutFailure("Selector y boton solapados");
                }
            }
            var toolButtons=new[]{contextButton,observe30,observe60,referenceButton,compareButton};
            foreach(var b in toolButtons)if(!b.Parent.ClientRectangle.Contains(b.Bounds))return LayoutFailure("Botón recortado: "+b.Text);
            for(int i=0;i<toolButtons.Length;i++)for(int j=i+1;j<toolButtons.Length;j++)if(toolButtons[i].Bounds.IntersectsWith(toolButtons[j].Bounds))return LayoutFailure("Botones de herramientas solapados");
            incident.IsNow=false;UpdateWindowInput();PerformLayout();Application.DoEvents();
            bool validHistorical=inputPanel.ClientRectangle.Contains(historicalWindow.Bounds);incident.IsNow=true;UpdateWindowInput();
            if(!validHistorical)return LayoutFailure("Hora de incidencia recortada");
            return true;
        }
        static bool LayoutFailure(string message){Console.Error.WriteLine(message);return false;}
        internal void SimulateScale(float factor){
            var controls=new List<Control>();Action<Control> collect=null;collect=c=>{controls.Add(c);foreach(Control child in c.Controls)collect(child);};collect(this);
            var fonts=controls.Select(c=>c.Font).ToArray();
            SuspendLayout();Scale(new SizeF(factor,factor));
            for(int i=0;i<controls.Count;i++)controls[i].Font=new Font(fonts[i].FontFamily,fonts[i].SizeInPoints*factor,fonts[i].Style);
            ResumeLayout(true);PerformLayout();
        }
    }
    static class Program {
        [STAThread]static int Main(string[] args){
            try {
                if(args.Length==2&&(args[0]=="--observe-worker"||args[0]=="--observe-test-worker")){int seconds;if(!int.TryParse(args[1],out seconds)||seconds<1||seconds>60)return 2;return ObservationCollector.Worker(seconds,args[0]=="--observe-test-worker");}
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                if(args.Length>0&&args[0]=="--self-test"){PipeText.Utf8();return SelfTest();}
                if(args.Length==2&&(args[0]=="--preview-observation"||args[0]=="--preview-context")){
                    using(Form preview=args[0]=="--preview-observation"?(Form)new ObservationForm(60,true):new IncidentDialog(new IncidentContext(),0)){preview.ShowInTaskbar=false;preview.Opacity=0;preview.Show();Application.DoEvents();using(var bitmap=new Bitmap(preview.Width,preview.Height)){preview.DrawToBitmap(bitmap,new Rectangle(Point.Empty,preview.Size));bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);}preview.Close();}return 0;
                }
                var form=new MainForm();
                if(args.Length>1&&args[0]=="--preview"){
                    form.LoadPreview();form.ShowInTaskbar=false;form.Opacity=0;
                    form.Show();Application.DoEvents();
                    using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);}
                    form.Close();form.Dispose();return 0;
                }
                Application.Run(form);return 0;
            }catch(Exception ex){if(args.Length>0){Console.Error.WriteLine(ex);return 1;}MessageBox.Show("No se ha podido abrir Pulso IT.\n\n"+ex.Message,"Pulso IT",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
        static int SelfTest(){
            var e=new Engine();if(Catalog.Sections.Length!=12||Catalog.Quick.Length!=5)return 1;
            if(Catalog.Profiles.Length!=Catalog.Symptoms.Length||Catalog.Profiles.Any(p=>p.Length==0||p.Distinct().Count()!=p.Length||p.Any(id=>!Catalog.Sections.Any(s=>s.Id==id))))return 14;
            foreach(var s in Catalog.Sections){string c=e.Command(s.Id,"",443,"");if(!c.Contains("ConvertTo-Json")||c.Contains("Set-ExecutionPolicy")||c.Contains("Out-File")||c.Contains("Set-Content"))return 2;}
            if(Engine.Quote("x';throw 'bad")!="'x'';throw ''bad'")return 3;
            var result=Engine.Parse("pc","[{\"State\":\"warning\",\"Title\":\"Prueba\",\"Summary\":\"ñ á\",\"Evidence\":\"línea 1\\nlínea 2\",\"Next\":\"revisar\"}]");
            if(result.Count!=1||result[0].State!="warning"||!result[0].Evidence.Contains("\n"))return 4;
            if(Engine.Parse("pc","[]")[0].State!="unknown")return 5;
            bool rejected=false;try{Engine.Parse("pc","{}");}catch(FormatException){rejected=true;}if(!rejected)return 6;
            using(var f=new MainForm()){f.ShowInTaskbar=false;f.Opacity=0;f.Show();Application.DoEvents();if(!f.VerifyView())return 7;f.Close();}
            foreach(float scale in new[]{1f,1.25f,1.5f,2f})using(var f=new MainForm()){
                f.ShowInTaskbar=false;f.Opacity=0;f.Show();Application.DoEvents();f.SimulateScale(scale);
                if(!f.VerifyInputGeometry())return 15;
                f.Size=f.MinimumSize;f.PerformLayout();Application.DoEvents();if(!f.VerifyInputGeometry())return 16;f.Close();
            }
            var fake=new Engine(new Dictionary<string,object>{{"pc","[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false);ConvertTo-Json -InputObject @([pscustomobject]@{State='good';Title='Conexión de prueba';Summary='simulado';Evidence='ningún dato real';Next='prueba'}) -Compress"}});
            var synthetic=fake.Run("pc","",443,"",CancellationToken.None);if(synthetic.Count!=1||synthetic[0].Title!="Conexión de prueba")return 8;
            var slow=new Engine(new Dictionary<string,object>{{"pc","Start-Sleep -Seconds 20"}});
            using(var cts=new CancellationTokenSource()){
                cts.CancelAfter(500);bool cancelled=false;var timer=Stopwatch.StartNew();
                try{slow.Run("pc","",443,"",cts.Token);}catch(OperationCanceledException){cancelled=true;}
                if(!cancelled||timer.Elapsed.TotalSeconds>8)return 9;
            }
            if(!Engine.ValidTarget("srv-01.ejemplo.local")||!Engine.ValidTarget("::1")||Engine.ValidTarget("host:abc")||Engine.ValidTarget("x';whoami")||Engine.ValidTarget("a..b"))return 10;
            string streaming="[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false);[Console]::WriteLine('{\"State\":\"info\",\"Title\":\"Antes del bloqueo\",\"Summary\":\"parcial\",\"Evidence\":\"simulado\",\"Next\":\"repetir\"}');[Console]::Out.Flush();Start-Sleep -Seconds 20;";
            var blocked=new Engine(new Dictionary<string,object>{{"pc",streaming}});
            var received=new List<Finding>();bool timedOut=false;
            try{blocked.Run("pc","",443,"",CancellationToken.None,f=>received.Add(f),24,3);}catch(TimeoutException){timedOut=true;}
            if(!timedOut||received.Count!=1||received[0].Title!="Antes del bloqueo")return 11;
            received.Clear();bool wasCancelled=false;
            using(var cts=new CancellationTokenSource()){
                cts.CancelAfter(2500);try{blocked.Run("pc","",443,"",cts.Token,f=>received.Add(f));}catch(OperationCanceledException){wasCancelled=true;}
            }
            if(!wasCancelled||received.Count!=1)return 12;
            string longBody="#"+new string('x',60000)+"\n"+"[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false);ConvertTo-Json -InputObject @([pscustomobject]@{State='info';Title='Cuerpo largo';Summary='correcto';Evidence='simulado';Next='ninguno'}) -Compress";
            var large=new Engine(new Dictionary<string,object>{{"pc",longBody}});
            if(large.Run("pc","",443,"",CancellationToken.None)[0].Title!="Cuerpo largo")return 13;
            V2Tests.Run();AssessmentTests.Run();
            return 0;
        }
    }
}
