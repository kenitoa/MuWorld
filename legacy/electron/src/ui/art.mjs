export const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const paths = {
 library:'M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z',
 practice:'M5 20V10M12 20V4M19 20v-7M2 10h6M9 4h6M16 13h6',
 chart:'M4 19V5M4 19h17M8 15l4-5 4 2 5-7',
 arcade:'M7 8h10c3 0 5 9 3 10-2 1-4-2-5-2H9c-1 0-3 3-5 2-2-1 0-10 3-10ZM7 10v5M4.5 12.5h5M16 11h.01M19 14h.01',
 settings:'m10 3 4 0 1 3 3 1 3 3-2 3 0 3-3 2-3-1-3 1-3-2 0-3-2-3 3-3 3-1zM15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0',
 play:'m9 5 11 7-11 7z', pause:'M8 5v14M16 5v14',
 arrow:'M4 12h16M14 6l6 6-6 6', back:'M20 12H4M10 6l-6 6 6 6',
 chevron:'m9 5 7 7-7 7', down:'m6 9 6 6 6-6', check:'m5 12 4 4L19 6',
 search:'M10.5 17a6.5 6.5 0 1 0 0-13 6.5 6.5 0 0 0 0 13ZM16 16l5 5',
 heart:'M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1.1-1.1a5.5 5.5 0 0 0-7.8 7.8L12 21l8.8-8.6a5.5 5.5 0 0 0 0-7.8Z',
 volume:'m11 4-6 5H2v6h3l6 5zM15 8a6 6 0 0 1 0 8M18 5a10 10 0 0 1 0 14',
 headphones:'M3 14v-3a9 9 0 0 1 18 0v3M3 12h3v8H3zM18 12h3v8h-3z',
 clock:'M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0ZM12 7v5l3 2',
 music:'M9 18V5l11-2v12M9 5v4l11-2M9 18c0 2-7 4-7 1s7-4 7-1ZM20 15c0 2-7 4-7 1s7-4 7-1Z',
 reset:'M3 10a9 9 0 1 1 2 8M3 4v6h6',
 close:'m6 6 12 12M6 18 18 6',
 download:'M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5',
 upload:'M12 16V4m-5 5 5-5 5 5M4 16v5h16v-5',
 bolt:'m13 2-9 12h7l-1 8 10-13h-7z',
 target:'M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0ZM17 12a5 5 0 1 1-10 0 5 5 0 0 1 10 0ZM13 12h-2',
 leaf:'M20 3C9 2 3 7 5 14c2 7 14 6 15-11ZM4 21 15 10',
 keyboard:'M3 5h18v14H3zM6 9h1m3 0h1m3 0h1m3 0h1M6 12h1m3 0h1m3 0h1m3 0h1M7 16h10',
 shield:'m12 3 8 3v7c0 4-8 8-8 8s-8-4-8-8V6zM8 12l3 3 5-6',
 sun:'M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0ZM12 2v2m0 16v2M2 12h2m16 0h2M5 5l1.5 1.5M18 18l1.5 1.5M5 19l1.5-1.5M18 6l1.5-1.5',
 bookmark:'M6 3h12v18l-6-4-6 4z',
 dots:'M5 12h.01M12 12h.01M19 12h.01'
};
export function icon(name, cls='') { return `<svg class="icon ${cls}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${paths[name] || paths.music}"/></svg>`; }
export const logo = `<svg viewBox="0 0 44 44" class="brand-mark" aria-hidden="true"><rect width="44" height="44" rx="13" fill="currentColor"/><path d="M11 30V17q0-4 3-4t5 7l3 8 3-8q2-7 5-7t3 4v13" fill="none" stroke="#1b241c" stroke-width="4.2" stroke-linecap="round"/></svg>`;
let artCounter=0;
export function albumArt(song, cls='', decorative=true) {
 const id=`cover${artCounter++}`; const n=song.art ?? 0;
 const palettes=[['#c8c4ea','#eee9fa','#7c78b5'],['#e4b995','#fff0cb','#b97855'],['#acbea9','#e6ead1','#596c5b'],['#d89e99','#f3d0b8','#8c4e65'],['#a1bfd2','#e2edf0','#567b92']];
 const [bg,fg,dark]=palettes[n%5];
 let shape='';
 if(n===0) shape=`<ellipse cx="202" cy="252" rx="125" ry="42" fill="${dark}" opacity=".15"/><g transform="rotate(-22 200 180)"><rect x="120" y="75" width="160" height="227" rx="80" fill="url(#${id}g)"/><ellipse cx="200" cy="135" rx="80" ry="52" fill="none" stroke="${fg}" stroke-width="1.5"/>${Array.from({length:14},(_,i)=>`<path d="M${132+i*10} 125v135" stroke="${fg}" stroke-opacity=".22"/>`).join('')}<rect x="154" y="114" width="92" height="146" rx="46" fill="${bg}"/><path d="M166 229c-28-60-3-107 37-109" fill="none" stroke="${fg}" stroke-width="8" opacity=".65"/></g>`;
 if(n===1) shape=`<circle cx="200" cy="183" r="101" fill="url(#${id}g)"/>${Array.from({length:16},(_,i)=>`<path d="M75 ${220+i*6}Q200 ${165+i*7} 325 ${220+i*6}" fill="none" stroke="${fg}" stroke-width="3"/>`).join('')}<path d="M80 184h240" stroke="${dark}" stroke-opacity=".15"/>`;
 if(n===2) shape=`<circle cx="200" cy="189" r="111" fill="${fg}"/><circle cx="242" cy="145" r="96" fill="${bg}"/><path d="m88 283 97-142 130 142z" fill="url(#${id}g)"/><path d="m185 141 4 141h126" fill="${fg}" opacity=".48"/><path d="m86 284 227-1" stroke="${dark}" stroke-opacity=".4"/>`;
 if(n===3) shape=`${Array.from({length:11},(_,i)=>`<ellipse cx="200" cy="${115+i*14}" rx="${108-i*3}" ry="45" fill="${i%2?bg:fg}" stroke="${dark}" stroke-opacity=".2" transform="rotate(-20 200 ${115+i*14})"/>`).join('')}`;
 if(n===4) shape=`<circle cx="200" cy="182" r="94" fill="url(#${id}g)"/><path d="M57 217h286v94H57z" fill="${bg}"/>${Array.from({length:12},(_,i)=>`<path d="M${55+i*6} ${221+i*7}h${290-i*12}" stroke="${fg}" stroke-width="${1+i*.2}"/>`).join('')}<circle cx="295" cy="88" r="5" fill="${fg}"/>`;
 return `<svg class="album-art ${cls}" viewBox="0 0 400 400" ${decorative?'aria-hidden="true"':`role="img" aria-label="${escapeHtml(song.title)} 앨범 아트"`}><defs><linearGradient id="${id}g" x1="0" y1="0" x2="1" y2="1"><stop stop-color="${fg}"/><stop offset=".55" stop-color="${bg}"/><stop offset="1" stop-color="${dark}"/></linearGradient><filter id="${id}noise"><feTurbulence type="fractalNoise" baseFrequency=".75" numOctaves="3" stitchTiles="stitch"/><feColorMatrix type="saturate" values="0"/><feComponentTransfer><feFuncA type="linear" slope=".08"/></feComponentTransfer><feBlend in="SourceGraphic" mode="soft-light"/></filter></defs><rect width="400" height="400" fill="${bg}"/><g filter="url(#${id}noise)">${shape}</g><text x="28" y="36" font-family="sans-serif" font-size="9" letter-spacing="3" fill="${dark}">MUWORLD ORIGINALS</text><text x="28" y="355" font-family="sans-serif" font-size="23" font-weight="500" letter-spacing="-1" fill="${dark}">${escapeHtml(song.title)}</text><text x="29" y="375" font-family="sans-serif" font-size="8" letter-spacing="3" fill="${dark}">VOL. 0${n+1} / ${song.bpm} BPM</text><path d="M344 351h27m-6-6 6 6-6 6" stroke="${dark}" fill="none"/></svg>`;
}
export function formatTime(us) { const s=Math.max(0,Math.floor(us/1e6)); return `${Math.floor(s/60)}:${String(s%60).padStart(2,'0')}`; }
export function formatMs(v) { return v==null?'—':`${v>0?'+':''}${Number(v).toFixed(1)}`; }
