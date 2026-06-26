const E=l=>l?l.replace(/[\t\n\f\r ]+/g," ").replace(/^[\t\n\f\r ]+/,"").replace(/[\t\n\f\r ]+$/,""):"",m=l=>E(l==null?void 0:l.textContent),T={XLINK:"http://www.w3.org/1999/xlink",EPUB:"http://www.idpf.org/2007/ops"},L={XML:"application/xml",XHTML:"application/xhtml+xml"},p={strong:["strong","self"],emphasis:["em","self"],style:["span","self"],a:"anchor",strikethrough:["s","self"],sub:["sub","self"],sup:["sup","self"],code:["code","self"],image:"image"},k={tr:["tr",{th:["th",p,["colspan","rowspan","align","valign"]],td:["td",p,["colspan","rowspan","align","valign"]]},["align"]]},N={epigraph:["blockquote"],subtitle:["h2",p],"text-author":["p",p],date:["p",p],stanza:"stanza"},v={title:["header",{p:["h1",p],"empty-line":["br"]}],epigraph:["blockquote","self"],image:"image",annotation:["aside"],section:["section","self"],p:["p",p],poem:["blockquote",N],subtitle:["h2",p],cite:["blockquote","self"],"empty-line":["br"],table:["table",k],"text-author":["p",p]};N.epigraph.push(v);const D={image:"image",title:["section",{p:["h1",p],"empty-line":["br"]}],epigraph:["section",v],section:["section",v]};class ${constructor(e){this.fb2=e,this.doc=document.implementation.createDocument(T.XHTML,"html"),this.bins=new Map(Array.from(this.fb2.getElementsByTagName("binary"),n=>[n.id,n]))}getImageSrc(e){const n=e.getAttributeNS(T.XLINK,"href");if(!n)return"data:,";const[,a]=n.split("#");if(!a)return n;const s=this.bins.get(a);return s?`data:${s.getAttribute("content-type")};base64,${s.textContent}`:n}image(e){const n=this.doc.createElement("img");return n.alt=e.getAttribute("alt"),n.title=e.getAttribute("title"),n.setAttribute("src",this.getImageSrc(e)),n}anchor(e){const n=this.convert(e,{a:["a",p]});return n.setAttribute("href",e.getAttributeNS(T.XLINK,"href")),e.getAttribute("type")==="note"&&n.setAttributeNS(T.EPUB,"epub:type","noteref"),n}stanza(e){const n=this.convert(e,{stanza:["p",{title:["header",{p:["strong",p],"empty-line":["br"]}],subtitle:["p",p]}]});for(const a of e.children)a.nodeName==="v"&&(n.append(this.doc.createTextNode(a.textContent)),n.append(this.doc.createElement("br")));return n}convert(e,n){if(e.nodeType===3)return this.doc.createTextNode(e.textContent);if(e.nodeType===4)return this.doc.createCDATASection(e.textContent);if(e.nodeType===8)return this.doc.createComment(e.textContent);const a=n==null?void 0:n[e.nodeName];if(!a)return null;if(typeof a=="string")return this[a](e);const[s,u,g]=a,d=this.doc.createElement(s);if(e.id&&(d.id=e.id),d.classList.add(e.nodeName),Array.isArray(g))for(const f of g){const A=e.getAttribute(f);A&&d.setAttribute(f,A)}const w=u==="self"?n:u;let y=e.firstChild;for(;y;){const f=this.convert(y,w);f&&d.append(f),y=y.nextSibling}return d}}const O=async l=>{var g;const e=await l.arrayBuffer(),n=new TextDecoder("utf-8").decode(e),a=new DOMParser,s=a.parseFromString(n,L.XML),u=s.xmlEncoding||((g=n.match(/^<\?xml\s+version\s*=\s*["']1.\d+"\s+encoding\s*=\s*["']([A-Za-z0-9._-]*)["']/))==null?void 0:g[1]);if(u&&u.toLowerCase()!=="utf-8"){const d=new TextDecoder(u).decode(e);return a.parseFromString(d,L.XML)}return s},X=URL.createObjectURL(new Blob([`
@namespace epub "http://www.idpf.org/2007/ops";
body > img, section > img {
    display: block;
    margin: auto;
}
.title h1 {
    text-align: center;
}
body > section > .title, body.notesBodyType > .title {
    margin: 3em 0;
}
body.notesBodyType > section .title h1 {
    text-align: start;
}
body.notesBodyType > section .title {
    margin: 1em 0;
}
p {
    text-indent: 1em;
    margin: 0;
}
:not(p) + p, p:first-child {
    text-indent: 0;
}
.poem p {
    text-indent: 0;
    margin: 1em 0;
}
.text-author, .date {
    text-align: end;
}
.text-author:before {
    content: "—";
}
table {
    border-collapse: collapse;
}
td, th {
    padding: .25em;
}
a[epub|type~="noteref"] {
    font-size: .75em;
    vertical-align: super;
}
body:not(.notesBodyType) > .title, body:not(.notesBodyType) > .epigraph {
    margin: 3em 0;
}
`],{type:"text/css"})),z=l=>`<?xml version="1.0" encoding="utf-8"?>
<html xmlns="http://www.w3.org/1999/xhtml">
    <head><link href="${X}" rel="stylesheet" type="text/css"/></head>
    <body>${l}</body>
</html>`,M="data-foliate-id",I=async l=>{const e={},n=await O(l),a=new $(n),s=t=>n.querySelector(t),u=t=>[...n.querySelectorAll(t)],g=t=>{const o=m(t.querySelector("nickname"));if(o)return o;const r=m(t.querySelector("first-name")),i=m(t.querySelector("middle-name")),c=m(t.querySelector("last-name")),b=[r,i,c].filter(h=>h).join(" "),x=c?[c,[r,i].filter(h=>h).join(" ")].join(", "):null;return{name:b,sortAs:x}},d=t=>(t==null?void 0:t.getAttribute("value"))??m(t),w=s("title-info annotation");if(e.metadata={title:m(s("title-info book-title")),identifier:m(s("document-info id")),language:m(s("title-info lang")),author:u("title-info author").map(g),translator:u("title-info translator").map(g),contributor:u("document-info author").map(g).concat(u("document-info program-used").map(m)).map(t=>Object.assign(typeof t=="string"?{name:t}:t,{role:"bkp"})),publisher:m(s("publish-info publisher")),published:d(s("title-info date")),modified:d(s("document-info date")),description:w?a.convert(w,{annotation:["div",v]}).innerHTML:null,subject:u("title-info genre").map(m)},s("coverpage image")){const t=a.getImageSrc(s("coverpage image"));e.getCover=()=>fetch(t).then(o=>o.blob())}else e.getCover=()=>null;const y=Array.from(n.querySelectorAll("body"),t=>{const o=a.convert(t,{body:["body",D]});return[Array.from(o.children,r=>{const i=[r,...r.querySelectorAll("[id]")].map(c=>c.id);return{el:r,ids:i}}),o]}),f=[],A=y[0][0].map(({el:t,ids:o})=>{const r=Array.from(t.querySelectorAll(":scope > section > .title"),(i,c)=>(i.setAttribute(M,c),{title:m(i),index:c}));return{ids:o,titles:r,el:t}}).concat(y.slice(1).map(([t,o])=>{const r=t.map(i=>i.ids).flat();return o.classList.add("notesBodyType"),{ids:r,el:o,linear:"no"}})).map(({ids:t,titles:o,el:r,linear:i})=>{var B;const c=z(r.outerHTML),b=new Blob([c],{type:L.XHTML}),x=URL.createObjectURL(b);f.push(x);const h=E(((B=r.querySelector(".title, .subtitle, p"))==null?void 0:B.textContent)??(r.classList.contains("title")?r.textContent:""));return{ids:t,title:h,titles:o,load:()=>x,createDocument:()=>new DOMParser().parseFromString(c,L.XHTML),size:b.size-Array.from(r.querySelectorAll("[src]"),C=>{var S;return((S=C.getAttribute("src"))==null?void 0:S.length)??0}).reduce((C,S)=>C+S,0),linear:i}}),q=new Map;return e.sections=A.map((t,o)=>{const{ids:r,load:i,createDocument:c,size:b,linear:x}=t;for(const h of r)h&&q.set(h,o);return{id:o,load:i,createDocument:c,size:b,linear:x}}),e.toc=A.map(({title:t,titles:o},r)=>{const i=r.toString();return{label:t,href:i,subitems:o!=null&&o.length?o.map(({title:c,index:b})=>({label:c,href:`${i}#${b}`})):null}}).filter(t=>t),e.resolveHref=t=>{const[o,r]=t.split("#");return o?{index:Number(o),anchor:i=>i.querySelector(`[${M}="${r}"]`)}:{index:q.get(r),anchor:i=>i.getElementById(r)}},e.splitTOCHref=t=>{var o;return((o=t==null?void 0:t.split("#"))==null?void 0:o.map(r=>Number(r)))??[]},e.getTOCFragment=(t,o)=>t.querySelector(`[${M}="${o}"]`),e.isExternal=t=>/^\w+:/i.test(t),e.destroy=()=>{for(const t of f)URL.revokeObjectURL(t)},e};export{I as makeFB2};
