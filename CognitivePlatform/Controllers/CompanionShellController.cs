using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

/// <summary>Public static shell contains no workspace data; every data route is separately authenticated.</summary>
[ApiController]
public sealed class CompanionShellController : ControllerBase
{
    [HttpGet("companion")]
    public IActionResult Index()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Content(CompanionHtml, "text/html; charset=utf-8");
    }

    [HttpGet("companion/app.js")]
    public IActionResult Script()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Content(CompanionScript, "text/javascript; charset=utf-8");
    }

    private const string CompanionHtml = """
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Axiom read-only companion</title><style>
        :root{color-scheme:light dark;font:16px system-ui,sans-serif}body{margin:0;padding:1rem;max-width:1200px;margin-inline:auto}
        button,input,select{font:inherit;padding:.65rem;min-height:44px;max-width:100%;box-sizing:border-box}button{cursor:pointer}label{display:block;margin:.5rem 0}
        header{display:flex;flex-wrap:wrap;gap:.6rem;align-items:center}h1{font-size:1.4rem}#layout{display:grid;grid-template-columns:18rem minmax(0,1fr);gap:1.5rem}
        aside{overflow-wrap:anywhere}#files button,#hits button{display:block;width:100%;text-align:left;margin:.25rem 0}#reader{overflow-wrap:anywhere}pre{white-space:pre-wrap;overflow-wrap:anywhere}
        table{border-collapse:collapse;display:block;overflow:auto}td,th{border:1px solid;padding:.4rem}#freshness,#status{white-space:pre-wrap;overflow-wrap:anywhere}#status{padding:.75rem;border:1px solid}
        [hidden]{display:none!important}@media(max-width:720px){#layout{grid-template-columns:1fr}aside{max-height:45vh;overflow:auto}body{padding:.75rem}}
        </style><script src="/companion/app.js" defer></script></head><body>
        <header><h1>Axiom read-only companion</h1><button id="disconnect" hidden>Disconnect and clear</button></header>
        <p id="identity"></p><p id="status" role="status" aria-live="polite">Authenticate to view explicitly shared saved Markdown. Unsaved desktop buffers are not published.</p>
        <section id="login"><label for="key">Dedicated companion credential (memory only)</label><input id="key" type="password" autocomplete="off"><button id="connect">Connect</button></section>
        <section id="connected" hidden><label for="workspace">Shared workspace</label><select id="workspace"></select>
        <div id="layout"><aside><label for="query">Literal search in saved documents</label><input id="query" maxlength="512"><button id="search">Search</button><button id="more" hidden>Search next batch</button>
        <div id="hits" aria-label="Search results"></div><h2>Workspace tree</h2><div id="files"></div></aside>
        <main><button id="refresh" disabled>Refresh current document</button><button id="source" disabled>Show source</button><p id="freshness"></p><pre id="metadata" aria-label="Saved classification and relationship context"></pre><article id="reader" aria-label="Saved document preview"></article><pre id="markdown" hidden></pre></main></div></section>
        </body></html>
        """;

    private const string CompanionScript = """
        'use strict';
        const element=id=>document.getElementById(id);
        let key='', currentPath=null, currentHash=null, currentWorkspace=null, nextOffset=null, sessionGeneration=0, workspaceGeneration=0, readGeneration=0, searchGeneration=0, lastQuery='';
        element('identity').textContent=`Server: ${location.origin}. Read-only; no editing, account upload or synchronized copy.`;
        function clearSession(message){sessionGeneration++;workspaceGeneration++;readGeneration++;searchGeneration++;key='';currentPath=null;currentHash=null;currentWorkspace=null;nextOffset=null;
          for(const id of ['files','hits','reader','markdown','freshness','metadata'])element(id).replaceChildren();
          element('reader').hidden=false;element('markdown').hidden=true;element('refresh').disabled=element('source').disabled=true;element('source').textContent='Show source';element('key').value='';element('workspace').replaceChildren();element('connected').hidden=true;element('login').hidden=false;element('disconnect').hidden=true;element('status').textContent=message;}
        async function request(route,body){const generation=sessionGeneration, scope=workspaceGeneration, submittedKey=key;const cancellation=new AbortController(), timer=setTimeout(()=>cancellation.abort(),12000);
          try{const response=await fetch('/api/companion/'+route,{method:body?'POST':'GET',headers:{'X-Companion-Key':key,...(body?{'Content-Type':'application/json'}:{})},
            body:body?JSON.stringify(body):undefined,credentials:'omit',cache:'no-store',redirect:'error',signal:cancellation.signal});
            if(generation!==sessionGeneration||scope!==workspaceGeneration||submittedKey!==key)throw new Error('Discarded old session response');if([401,403,503].includes(response.status)){clearSession(`Access unavailable or revoked (HTTP ${response.status}). Displayed content and credential cleared.`);throw new Error('Access unavailable');}
            if(generation!==sessionGeneration||scope!==workspaceGeneration||submittedKey!==key)throw new Error('Discarded old session response');if(!response.ok)throw new Error(`Request failed (HTTP ${response.status}).`);const data=await response.json();if(generation!==sessionGeneration||scope!==workspaceGeneration||submittedKey!==key)throw new Error('Discarded old session response');return data;
          }finally{clearTimeout(timer);}}
        async function run(operation){try{await operation();}catch(error){element('status').textContent=key?'Service unavailable; last displayed snapshot is retained in memory only. Refresh to check freshness.': 'Access unavailable. Check the dedicated credential and explicitly shared workspace configuration.';}}
        function sourceButton(label,path,parent){const button=document.createElement('button');button.type='button';button.textContent=label;button.title=path;button.onclick=()=>run(()=>read(path));parent.append(button);}
        async function loadWorkspace(){workspaceGeneration++;readGeneration++;searchGeneration++;lastQuery='';currentWorkspace=element('workspace').value;currentPath=null;currentHash=null;nextOffset=null;
          for(const id of ['files','hits','reader','markdown','freshness','metadata'])element(id).replaceChildren();element('refresh').disabled=element('source').disabled=true;element('more').hidden=true;
          const data=await request('files?'+new URLSearchParams({workspaceId:currentWorkspace}));const groups=new Map([['',element('files')]]);
          for(const file of data.files){let prefix='',parent=element('files');const parts=file.path.split('/');
            for(const folder of parts.slice(0,-1)){prefix+=folder+'/';if(!groups.has(prefix)){const details=document.createElement('details'),summary=document.createElement('summary');summary.textContent=folder;details.append(summary);parent.append(details);groups.set(prefix,details);}parent=groups.get(prefix);}
            sourceButton(parts.at(-1),file.path,parent);}
          element('status').textContent=`${data.files.length} saved Markdown files. Snapshots update only when you read/refresh. Disconnect or page closure discards session content.`;}
        async function read(path){const generation=++readGeneration;const data=await request('document?'+new URLSearchParams({workspaceId:currentWorkspace,path}));
          if(generation!==readGeneration)return;const changed=currentPath===path&&currentHash!==null&&currentHash!==data.contentHash;currentPath=path;currentHash=data.contentHash;
          const parsed=new DOMParser().parseFromString(data.html,'text/html');
          for(const checkbox of parsed.querySelectorAll('input[type="checkbox"]'))checkbox.replaceWith(document.createTextNode(checkbox.hasAttribute('checked')?'[x] ':'[ ] '));
          for(const unsafe of parsed.querySelectorAll('script,iframe,object,embed,form,input,button,style,meta,link,svg,math,img'))unsafe.remove();
          for(const node of parsed.querySelectorAll('*'))for(const attribute of [...node.attributes])if(!['class','start','colspan','rowspan'].includes(attribute.name))node.removeAttribute(attribute.name);
          for(const anchor of parsed.querySelectorAll('a'))anchor.replaceWith(document.createTextNode(anchor.textContent));
          element('reader').replaceChildren(...[...parsed.body.childNodes].map(node=>document.importNode(node,true)));element('markdown').textContent=data.markdown;
          const metadata=data.metadata;element('metadata').textContent=metadata?.available?`Saved metadata at ${metadata.readUtc}; revision ${metadata.revision??'none'}\nType: ${metadata.type??'none'}\nTags: ${metadata.tags.join(', ')}\nAliases: ${metadata.aliases.join(', ')}\n${metadata.relationships.map(relation=>`${relation.direction}: ${relation.kind} · ${relation.relatedDocumentId} (access not implied)`).join('\n')}`:metadata?.status??'Metadata context unavailable.';
          element('freshness').textContent=`${path}\nRead ${data.readUtc}\nSHA-256 ${data.contentHash}\n${changed?'Changed since previous read.':'Saved source snapshot; refresh to check for changes.'}${data.documentId?'\nAxiom document ID: '+data.documentId:''}`;
          element('refresh').disabled=element('source').disabled=false;element('status').textContent='Read-only preview. Active URLs/images are omitted; source text is available. No Markdown or metadata is changed.';}
        async function search(offset){const query=element('query').value;if(!query.trim())return;if(query!==lastQuery)offset=0;lastQuery=query;const generation=++searchGeneration;const data=await request('search',{workspaceId:currentWorkspace,query,offset});
          if(generation!==searchGeneration)return;if(offset===0)element('hits').replaceChildren();for(const hit of data.matches)sourceButton(`${hit.path} · line ${hit.lineNumber}\n${hit.excerpt}`,hit.path,element('hits'));
          nextOffset=data.nextOffset;element('more').hidden=nextOffset===null;element('status').textContent=`Searched ${data.scanned} saved documents in this batch. ${nextOffset===null?'All batches scanned.':'More files remain; choose Search next batch.'} File changes can alter batch ordering; restart search for a fresh inventory.`;}
        element('connect').onclick=()=>run(async()=>{sessionGeneration++;key=element('key').value;element('key').value='';const data=await request('workspaces');
          if(data.protocolVersion!==1)throw new Error('Unsupported protocol');element('workspace').replaceChildren();for(const workspace of data.workspaces){const option=document.createElement('option');option.value=workspace.id;option.textContent=workspace.label;element('workspace').append(option);}
          element('login').hidden=true;element('connected').hidden=false;element('disconnect').hidden=false;if(data.workspaces.length)await loadWorkspace();else element('status').textContent='Authenticated; no workspace is explicitly shared.';});
        element('disconnect').onclick=()=>clearSession('Disconnected; credential and displayed content discarded.');element('workspace').onchange=()=>run(loadWorkspace);
        element('search').onclick=()=>run(()=>search(0));element('more').onclick=()=>run(()=>search(nextOffset??0));element('refresh').onclick=()=>currentPath&&run(()=>read(currentPath));
        element('source').onclick=()=>{element('markdown').hidden=!element('markdown').hidden;element('reader').hidden=!element('markdown').hidden;element('source').textContent=element('markdown').hidden?'Show source':'Show preview';};
        window.addEventListener('pagehide',()=>clearSession('Session discarded.'));
        """;
}
