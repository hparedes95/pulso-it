using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PulsoIT {
    public sealed class IncidentContext {
        public string Symptom="Sin concretar",Affected="Sin concretar",Change="No se sabe",Notes="";
        public bool IsNow=true;
        public DateTime IncidentLocal=DateTime.Now;
        public int MarginMinutes=15;
        public string EvidenceKey {get{return IsNow?"now":IncidentLocal.ToUniversalTime().ToString("o")+"|"+MarginMinutes;}}
        public DateTime? FromUtc {get{return IsNow?(DateTime?)null:IncidentLocal.AddMinutes(-MarginMinutes).ToUniversalTime();}}
        public DateTime? ToUtc {get{return IsNow?(DateTime?)null:IncidentLocal.AddMinutes(MarginMinutes).ToUniversalTime();}}
        public string Describe(){return "Síntoma: "+Symptom+Environment.NewLine+"Alcance: "+Affected+Environment.NewLine+"Momento: "+(IsNow?"está ocurriendo ahora":IncidentLocal.ToString("g")+" ± "+MarginMinutes+" min")+Environment.NewLine+"Cambio conocido: "+Change+(string.IsNullOrWhiteSpace(Notes)?"":" · "+Notes);}
        public IncidentContext Copy(){return (IncidentContext)MemberwiseClone();}
    }
    public sealed class IncidentDialog:Form {
        readonly ComboBox symptom=new ComboBox(),affected=new ComboBox(),change=new ComboBox(),when=new ComboBox();
        readonly DateTimePicker at=new DateTimePicker();readonly NumericUpDown margin=new NumericUpDown();readonly TextBox notes=new TextBox();
        public IncidentContext Value;
        public IncidentDialog(IncidentContext existing,int selected=-1){
            Text="Contexto de la incidencia · solo en memoria";Font=new Font("Segoe UI",10);AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(660,470);MinimumSize=new Size(600,500);MaximizeBox=false;MinimizeBox=false;
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=2,RowCount=9};root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(root);
            for(int i=0;i<8;i++)root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var intro=new Label{AutoSize=true,Dock=DockStyle.Fill,Text="Estas respuestas ayudan a acotar las consultas. No se guardan al cerrar la aplicación.",Margin=new Padding(0,0,0,18)};root.Controls.Add(intro,0,0);root.SetColumnSpan(intro,2);
            Add(root,"¿Qué ocurre?",symptom,1);symptom.Items.Add("Sin concretar");symptom.Items.AddRange(Catalog.Symptoms);symptom.SelectedIndex=selected>=0?selected+1:Math.Max(0,symptom.Items.IndexOf(existing.Symptom));
            Add(root,"¿A quién afecta?",affected,2);affected.Items.AddRange(new object[]{"Sin concretar","Solo este equipo","Solo un usuario","Varios equipos o usuarios"});affected.SelectedIndex=Math.Max(0,affected.Items.IndexOf(existing.Affected));
            Add(root,"¿Cuándo ocurre?",when,3);when.Items.AddRange(new object[]{"Está ocurriendo ahora","Ocurrió a una hora concreta"});when.SelectedIndex=existing.IsNow?0:1;
            at.Format=DateTimePickerFormat.Custom;at.CustomFormat="dd/MM/yyyy HH:mm:ss";at.Value=existing.IncidentLocal;at.Dock=DockStyle.Fill;root.Controls.Add(new Label{Text="Hora de la incidencia",AutoSize=true,Margin=new Padding(0,8,0,8)},0,4);root.Controls.Add(at,1,4);
            margin.Minimum=5;margin.Maximum=120;margin.Increment=5;margin.Value=existing.MarginMinutes;margin.Dock=DockStyle.Fill;root.Controls.Add(new Label{Text="Margen ± minutos",AutoSize=true,Margin=new Padding(0,8,0,8)},0,5);root.Controls.Add(margin,1,5);
            Add(root,"¿Hubo cambios?",change,6);change.Items.AddRange(new object[]{"No se sabe","Ninguno conocido","Actualización de Windows","Controlador o hardware","Aplicación instalada o actualizada","Red o VPN","Otro cambio"});change.SelectedIndex=Math.Max(0,change.Items.IndexOf(existing.Change));
            notes.Text=existing.Notes;notes.MaxLength=240;notes.Dock=DockStyle.Fill;root.Controls.Add(new Label{Text="Detalle opcional",AutoSize=true,Margin=new Padding(0,8,0,8)},0,7);root.Controls.Add(notes,1,7);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,FlowDirection=FlowDirection.RightToLeft};var ok=new Button{Text="Aplicar",AutoSize=true,Padding=new Padding(12,6,12,6)};var back=new Button{Text="Volver",AutoSize=true,DialogResult=DialogResult.Cancel,Padding=new Padding(12,6,12,6)};buttons.Controls.Add(ok);buttons.Controls.Add(back);root.Controls.Add(buttons,0,8);root.SetColumnSpan(buttons,2);AcceptButton=ok;CancelButton=back;
            when.SelectedIndexChanged+=(s,e)=>{at.Enabled=margin.Enabled=when.SelectedIndex==1;};at.Enabled=margin.Enabled=when.SelectedIndex==1;
            ok.Click+=(s,e)=>{if(when.SelectedIndex==1&&at.Value>DateTime.Now.AddMinutes(1)){MessageBox.Show(this,"La hora de la incidencia no puede estar en el futuro.");return;}Value=new IncidentContext{Symptom=symptom.Text,Affected=affected.Text,Change=change.Text,Notes=notes.Text.Trim(),IsNow=when.SelectedIndex==0,IncidentLocal=DateTime.SpecifyKind(at.Value,DateTimeKind.Local),MarginMinutes=(int)margin.Value};DialogResult=DialogResult.OK;Close();};
        }
        static void Add(TableLayoutPanel panel,string text,ComboBox combo,int row){combo.DropDownStyle=ComboBoxStyle.DropDownList;combo.Dock=DockStyle.Fill;combo.Margin=new Padding(0,5,0,8);panel.Controls.Add(new Label{Text=text,AutoSize=true,Margin=new Padding(0,8,10,8)},0,row);panel.Controls.Add(combo,1,row);}
    }
    public sealed class MemoryReference {
        public DateTime Captured;
        public List<Finding> Findings;
        public ObservationSession Observation;
        public static MemoryReference Capture(IEnumerable<Finding> findings,ObservationSession observation){return new MemoryReference{Captured=DateTime.Now,Findings=findings.Select(f=>f.Copy()).ToList(),Observation=observation};}
        static string Key(Finding f){return f.Section+"|"+f.ScopeKey+"|"+f.IncidentKey+"|"+(string.IsNullOrEmpty(f.Code)?f.Title:f.Code)+"|"+f.MetaText("Identity");}
        public string Compare(IEnumerable<Finding> current,ObservationSession observation){
            var lines=new List<string>{"COMPARACIÓN SOLO EN MEMORIA", "Referencia: "+Captured.ToString("g"),"Las diferencias no demuestran por sí solas la causa de la incidencia.",""};
            var newer=current.Where(f=>f.Time>Captured).ToList();var before=Findings.GroupBy(Key).ToDictionary(g=>g.Key,g=>g.Last());int changed=0,matched=0;
            foreach(var f in newer){Finding old;if(!before.TryGetValue(Key(f),out old)){lines.Add("NUEVO · "+Catalog.Get(f.Section).Name+" · "+f.Title+"\r\n"+f.Summary+"\r\n");continue;}
                if(f.Code=="event.group"){DateTime a,b,c,d;if(DateTime.TryParse(old.MetaText("WindowStartUtc"),out a)&&DateTime.TryParse(old.MetaText("WindowEndUtc"),out b)&&DateTime.TryParse(f.MetaText("WindowStartUtc"),out c)&&DateTime.TryParse(f.MetaText("WindowEndUtc"),out d)&&Math.Abs(((b-a)-(d-c)).TotalMinutes)>1){lines.Add("NO COMPARABLE · "+f.Title+" usa una ventana de eventos de distinta duración.\r\n");continue;}}matched++;
                if(old.State!=f.State||old.Comparable()!=f.Comparable()){changed++;lines.Add("CAMBIO · "+Catalog.Get(f.Section).Name+" · "+f.Title+"\r\nAntes ("+old.Time.ToString("T")+"): "+Finding.Label(old.State)+" · "+old.Summary+"\r\nDespués ("+f.Time.ToString("T")+"): "+Finding.Label(f.State)+" · "+f.Summary+"\r\n");}
            }
            var refreshed=new HashSet<string>(newer.Select(f=>f.Section+"|"+f.ScopeKey+"|"+f.IncidentKey));var nowKeys=new HashSet<string>(newer.Select(Key));
            foreach(var old in Findings.Where(f=>refreshed.Contains(f.Section+"|"+f.ScopeKey+"|"+f.IncidentKey)&&!nowKeys.Contains(Key(f))))lines.Add("YA NO CONSTA · "+old.Title+". Puede deberse a un cambio, filtros o cobertura incompleta; no se considera resuelto automáticamente.\r\n");
            lines.Insert(4,matched+" comprobaciones comparables · "+changed+" modificadas. Solo se comparan consultas posteriores y del mismo equipo/destino.");
            if(!newer.Any())lines.Add("Vuelve a consultar una sección después de guardar la referencia. Las consultas antiguas no se comparan como si fueran nuevas.");
            if(Observation!=null&&observation!=null&&observation.Started>Captured){lines.Add("OBSERVACIÓN DE RENDIMIENTO · Antes y después\r\n"+Observation.ShortSummary()+"\r\n"+observation.ShortSummary()+"\r\nLos intervalos pueden corresponder a cargas de trabajo distintas.");}
            return string.Join(Environment.NewLine,lines);
        }
    }
}
