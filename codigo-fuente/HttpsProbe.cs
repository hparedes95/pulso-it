using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace PulsoIT {
    public static class HttpsProbe {
        public static Finding Classify(string target,int port,string outcome,int? status,string evidence){
            string state=outcome=="blocked"?"unknown":outcome=="tls"||outcome=="failed"||outcome=="timeout"?"warning":status>=500?"warning":status>=400&&status!=401&&status!=403&&status!=404&&status!=405?"warning":"info";
            string summary=outcome=="tls"?"No se completó la validación TLS/certificado.":outcome=="blocked"?"El contexto local no tiene permiso para completar la prueba.":outcome=="timeout"?"La petición HTTPS agotó el tiempo.":status.HasValue?"El servicio respondió HTTP "+status.Value+" a HEAD /.":"No se pudo obtener respuesta HTTPS.";
            var f=new Finding("red",state,"HTTPS · "+target+":"+port,summary,evidence,"Se consulta solo HEAD /, sin credenciales, cookies ni seguimiento de redirecciones. 401/403 indica respuesta con requisito de autorización; 404/405 no prueba una caída. No se omite la validación del certificado ni se valida el funcionamiento de toda la aplicación."){Code="network.https",ScopeKey="local:"+Environment.MachineName+"|https:"+target+":"+port};
            f.Data=new Dictionary<string,object>{{"Identity",target+":"+port},{"Target",target},{"Port",port},{"Outcome",outcome},{"Status",status}};return f;
        }
        public static Finding Run(string target,int port,CancellationToken token){
            if(!Engine.ValidTarget(target)||string.IsNullOrEmpty(target)||port<1||port>65535)throw new ArgumentException("Destino HTTPS no válido.");
            var builder=new UriBuilder("https",target,port,"/");var request=(HttpWebRequest)WebRequest.Create(builder.Uri);
            request.Method="HEAD";request.AllowAutoRedirect=false;request.Timeout=6000;request.ReadWriteTimeout=6000;request.UseDefaultCredentials=false;request.Credentials=null;request.PreAuthenticate=false;request.CookieContainer=null;request.UserAgent="Pulso-IT/2.0";
            // 0 solicita la política TLS del sistema en .NET Framework 4.7 o posterior.
            ServicePointManager.SecurityProtocol=(SecurityProtocolType)0;
            string presented="";
            request.ServerCertificateValidationCallback=(sender,certificate,chain,errors)=>{
                if(certificate!=null)using(var cert=new X509Certificate2(certificate)){presented="Certificado presentado: "+cert.GetNameInfo(X509NameType.DnsName,false)+Environment.NewLine+"Válido desde: "+cert.NotBefore.ToString("g")+Environment.NewLine+"Válido hasta: "+cert.NotAfter.ToString("g")+Environment.NewLine+"Validación del sistema: "+errors;}
                return certificate!=null&&errors==System.Net.Security.SslPolicyErrors.None;
            };
            var proxy=WebRequest.GetSystemWebProxy();proxy.Credentials=null;request.Proxy=proxy;var clock=Stopwatch.StartNew();
            using(token.Register(()=>request.Abort())){
                try{using(var response=(HttpWebResponse)request.GetResponse()){return Response(target,port,response,request,clock.ElapsedMilliseconds);}}
                catch(WebException ex){token.ThrowIfCancellationRequested();var response=ex.Response as HttpWebResponse;if(response!=null)using(response){return Response(target,port,response,request,clock.ElapsedMilliseconds);}
                    var cause=ex.GetBaseException() as SocketException;string outcome=cause!=null&&cause.SocketErrorCode==SocketError.AccessDenied?"blocked":ex.Status==WebExceptionStatus.TrustFailure||ex.Status==WebExceptionStatus.SecureChannelFailure?"tls":ex.Status==WebExceptionStatus.Timeout?"timeout":"failed";
                    return Classify(target,port,outcome,null,ex.Message+Environment.NewLine+"Estado de transporte: "+ex.Status+Environment.NewLine+presented+Environment.NewLine+"La validación usa la confianza y el reloj del sistema; la infraestructura PKI o el proxy pueden intervenir.");}
            }
        }
        static Finding Response(string target,int port,HttpWebResponse response,HttpWebRequest request,long ms){
            string certificate="Certificado: no disponible.";if(request.ServicePoint.Certificate!=null)using(var cert=new X509Certificate2(request.ServicePoint.Certificate)){certificate="Certificado: "+cert.GetNameInfo(X509NameType.DnsName,false)+Environment.NewLine+"Emisor: "+cert.GetNameInfo(X509NameType.SimpleName,true)+Environment.NewLine+"Válido hasta: "+cert.NotAfter.ToString("g");}
            return Classify(target,port,"response",(int)response.StatusCode,"HEAD "+request.RequestUri.GetLeftPart(UriPartial.Path)+Environment.NewLine+"Respuesta: "+(int)response.StatusCode+" · "+ms+" ms"+Environment.NewLine+certificate);
        }
    }
}
