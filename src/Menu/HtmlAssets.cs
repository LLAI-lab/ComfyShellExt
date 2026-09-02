namespace ComfyShellExt.Menu
{
    /// <summary>Stylesheet and script for the generated workflow page, kept out of the builder.</summary>
    internal static partial class HtmlReport
    {
        private const string Css = @"
:root{color-scheme:dark}
*{box-sizing:border-box}
body{margin:0;padding:0 0 48px;background:#0b1020;color:#e5e7eb;
  font:14px/1.6 'Segoe UI','Microsoft YaHei',system-ui,sans-serif}
header{padding:22px 28px 18px;background:linear-gradient(120deg,#111827,#1e293b);
  border-bottom:1px solid #334155}
h1{margin:0 0 4px;font-size:20px;font-weight:600;word-break:break-all}
.path{color:#94a3b8;font-size:12px;word-break:break-all;font-family:Consolas,monospace}
.chips{margin-top:12px;display:flex;flex-wrap:wrap;gap:8px}
.chip{background:#0f172a;border:1px solid #22d3ee55;border-radius:999px;
  padding:2px 12px;font-size:12px;color:#cbd5e1}
.chip b{color:#22d3ee;font-weight:600;margin-right:6px}
main{max-width:1080px;margin:0 auto;padding:22px 28px;display:flex;flex-direction:column;gap:18px}
.card{background:#111827;border:1px solid #263449;border-radius:10px;padding:16px 18px}
.card h2{margin:0 0 12px;font-size:15px;color:#22d3ee;font-weight:600}
table{border-collapse:collapse;width:100%}
th,td{text-align:left;padding:6px 10px;border-bottom:1px solid #1f2b3d;vertical-align:top}
th{width:180px;color:#94a3b8;font-weight:500;white-space:nowrap}
td{word-break:break-all;font-family:Consolas,monospace}
tr:last-child th,tr:last-child td{border-bottom:none}
.prompts{display:block}
.p{margin-bottom:12px}
.p:last-child{margin-bottom:0}
.p h3{margin:0 0 6px;font-size:13px;font-weight:600}
.pos h3{color:#4ade80}
.neg h3{color:#fb7185}
.p pre{margin:0;padding:10px 12px;border-radius:8px;background:#0b1220;border:1px solid #1f2b3d;
  white-space:pre-wrap;word-break:break-word;font-family:Consolas,monospace;font-size:13px}
.pos pre{border-left:3px solid #4ade80}
.neg pre{border-left:3px solid #fb7185}
.tags{display:flex;flex-wrap:wrap;gap:6px}
.tags span{background:#0b1220;border:1px solid #1f2b3d;border-radius:6px;padding:2px 8px;
  font-size:12px;font-family:Consolas,monospace;color:#cbd5e1}
.jsonbar{display:flex;flex-wrap:wrap;align-items:center;gap:10px;margin-bottom:12px}
.jsonbar h2{margin:0}
.tabs{display:flex;gap:6px;flex-wrap:wrap}
.tabs button{background:#0b1220;color:#94a3b8;border:1px solid #263449;border-radius:6px;
  padding:4px 10px;font:inherit;font-size:12px;cursor:pointer}
.tabs button.on{color:#0b1020;background:#22d3ee;border-color:#22d3ee;font-weight:600}
.actions{margin-left:auto;display:flex;gap:8px}
.actions button,.actions a{background:#0b1220;color:#e5e7eb;border:1px solid #334155;border-radius:6px;
  padding:4px 12px;font:inherit;font-size:12px;cursor:pointer;text-decoration:none}
.actions button:hover,.actions a:hover{border-color:#22d3ee;color:#22d3ee}
#out{margin:0;padding:14px;max-height:60vh;overflow:auto;background:#0b1220;border:1px solid #1f2b3d;
  border-radius:8px;font-family:Consolas,monospace;font-size:12.5px;white-space:pre;color:#d1d5db}
";

        private const string Script = @"
(function(){
  var out=document.getElementById('out');
  var tabs=Array.prototype.slice.call(document.querySelectorAll('.tabs button'));
  var stemNode=document.getElementById('stem');
  var stem=stemNode?stemNode.textContent.trim():'workflow';
  var current='';
  var currentKey='wf';
  function raw(key){
    var node=document.getElementById('data-'+key);
    return node?node.textContent:'';
  }
  function show(key){
    currentKey=key;
    var text=raw(key);
    try{current=JSON.stringify(JSON.parse(text),null,2);}catch(e){current=text;}
    out.textContent=current;
    tabs.forEach(function(b){b.classList.toggle('on',b.getAttribute('data-key')===key);});
  }
  tabs.forEach(function(b){b.addEventListener('click',function(){show(b.getAttribute('data-key'));});});
  if(tabs.length)show(tabs.filter(function(b){return b.classList.contains('on');})[0]
    .getAttribute('data-key'));
  var copy=document.getElementById('copy');
  if(copy)copy.addEventListener('click',function(){
    var done=function(){copy.textContent='已复制';setTimeout(function(){copy.textContent='复制';},1400);};
    if(navigator.clipboard&&navigator.clipboard.writeText){
      navigator.clipboard.writeText(current).then(done,fallback);
    }else fallback();
    function fallback(){
      var area=document.createElement('textarea');
      area.value=current;document.body.appendChild(area);area.select();
      try{document.execCommand('copy');done();}catch(e){}
      document.body.removeChild(area);
    }
  });
  var save=document.getElementById('save');
  if(save)save.addEventListener('click',function(e){
    e.preventDefault();
    var name=stem+(currentKey==='pr'?'.prompt.json':'.workflow.json');
    var blob=new Blob([raw(currentKey)],{type:'application/json'});
    var url=URL.createObjectURL(blob);
    var link=document.createElement('a');
    link.href=url;link.download=name;document.body.appendChild(link);link.click();
    document.body.removeChild(link);setTimeout(function(){URL.revokeObjectURL(url);},4000);
  });
})();
";
    }
}
