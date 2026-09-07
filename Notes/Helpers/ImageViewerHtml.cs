namespace Notes.Helpers;

public static class ImageViewerHtml
{
  public const string CopyCodeCss =
      "pre{cursor:pointer;white-space:pre-wrap;overflow-wrap:break-word;word-break:break-all;overflow-x:hidden;}" +
      "code{cursor:pointer;}" +
      "pre.copy-ok{outline:2px solid #34C759;}" +
      "code.copy-ok{outline:2px solid #34C759;border-radius:3px;}";

  public const string CopyCodeScript = """
      <script>
      (function(){
        function doCopy(text, el) {
          var p = (navigator.clipboard && navigator.clipboard.writeText)
            ? navigator.clipboard.writeText(text) : Promise.reject();
          p.catch(function(){
            var ta = document.createElement('textarea');
            ta.value = text;
            ta.style.cssText = 'position:fixed;opacity:0;top:0;left:0;';
            document.body.appendChild(ta); ta.focus(); ta.select();
            try { document.execCommand('copy'); } catch(e){}
            document.body.removeChild(ta);
          });
          el.classList.add('copy-ok');
          setTimeout(function(){ el.classList.remove('copy-ok'); }, 700);
        }
        function attach() {
          document.querySelectorAll('pre').forEach(function(pre) {
            pre.addEventListener('click', function(e) {
              doCopy((pre.querySelector('code') || pre).innerText, pre);
              e.stopPropagation();
            });
          });
          document.querySelectorAll('code').forEach(function(code) {
            if (code.closest('pre')) return;
            code.addEventListener('click', function(e) {
              doCopy(code.innerText, code);
              e.stopPropagation();
            });
          });
        }
        if (document.readyState === 'loading')
          document.addEventListener('DOMContentLoaded', attach);
        else
          attach();
      })();
      </script>
      """;

#if WINDOWS
    public const string ViewerCss = "img { cursor: zoom-in; }";
    public const string ViewerDiv = "";
    // Encode the element ID (or fallback src URL) directly in the navigation URL —
    // sessionStorage is unreliable for file:// origins in WebView2.
    public const string ViewerScript = """
        <script>
        document.addEventListener('click', function(e) {
          if (e.target.tagName !== 'IMG' || !e.target.src) return;
          var mid = e.target.getAttribute('data-media-id') || '';
          var pg  = e.target.getAttribute('data-page');
          var src = e.target.src || '';
          // For media images use the media id; for others use the src URL
          // (skip data: URIs for non-media images — they are too large for a URL).
          var payload = mid ? ('media-' + mid + (pg ? ':' + pg : ''))
                      : src.startsWith('data:') ? ''
                      : src;
          if (!payload) return;
          window.location.href = 'img-viewer://open/' + encodeURIComponent(payload);
        });
        </script>
        """;
#else
    // CSS pieces are separate C# string literals, not a multi-line block: the C# preprocessor
    // scans skipped #if branches for directives and would mistake a line that starts with
    // "#_iv {" for one.
    public const string ViewerCss =
        "img:not(#_iv_img){cursor:pointer;-webkit-tap-highlight-color:transparent;}" +
        " #_iv{display:none;position:fixed;top:0;left:0;width:100%;height:100%;" +
            "background-color:#000;z-index:9999;overflow:hidden;touch-action:none;" +
            "user-select:none;-webkit-user-select:none;}" +
        " #_iv_img{position:absolute;left:0;top:0;transform-origin:0 0;pointer-events:none;" +
            "border-radius:4px;-webkit-user-drag:none;user-select:none;will-change:transform;}";

    public const string ViewerDiv = "<div id='_iv'><img id='_iv_img' alt=''/></div>";

    // Telegram-style fullscreen viewer: FLIP open/close from the thumbnail rect, rAF-batched
    // transforms, focal-point pinch, double-tap zoom, edge-clamped pan with rubber-band, and a
    // velocity-aware swipe-to-dismiss. Native contract is unchanged:
    //   swipe://disable | swipe://enable      - toggles the back-gesture while the viewer is up
    //   img-viewer://open/<mediaId[:page]>    - asks the host for the full-resolution data URI
    //   img-download://<mediaId>              - long-press on an inline image
    //   window._setViewerFullRes(src)         - host pushes the full-resolution image back
    public const string ViewerScript = """
        <script>
        (function(){
          var iv   = document.getElementById('_iv');
          var vImg = document.getElementById('_iv_img');

          var OPEN_MS=260, SPRING_MS=240, MAX_SCALE=4, DBL_ZOOM=2.5;
          var TAP_SLOP=10, DBL_MS=280, DBL_SLOP=30, RUBBER=0.35;
          var DISMISS_SHIFT=110, DISMISS_VEL=0.5, DISMISS_FADE=320, DISMISS_SHRINK=260;

          var state='closed';                 // 'closed' | 'open'
          var animating=false;
          var baseW=0, baseH=0, baseX=0, baseY=0;   // fitted rect at scale 1
          var scale=1, tx=0, ty=0, bg=1;            // tx/ty are screen-space px, origin 0 0
          var srcEl=null;                      // the inline thumbnail, for the close transition

          // one location.href navigation survives per tick, so serialize the bridge calls
          var navQ=[], navBusy=false;
          function nav(u){ navQ.push(u); pump(); }
          function pump(){
            if(navBusy || !navQ.length) return;
            navBusy=true;
            window.location.href = navQ.shift();
            setTimeout(function(){ navBusy=false; pump(); }, 16);
          }

          var pend=false;
          function schedule(){ if(!pend){ pend=true; requestAnimationFrame(function(){ pend=false; paint(); }); } }
          function paint(){
            vImg.style.transform =
              'translate(' + (baseX+tx) + 'px,' + (baseY+ty) + 'px) scale(' + scale + ')';
            iv.style.backgroundColor = 'rgba(0,0,0,' + bg + ')';
          }
          function noTransition(){ vImg.style.transition='none'; iv.style.transition='none'; }
          function withTransition(ms){
            vImg.style.transition = 'transform ' + ms + 'ms cubic-bezier(.2,0,0,1)';
            iv.style.transition   = 'background-color ' + ms + 'ms linear';
          }
          function animateTo(ns, nx, ny, nbg, ms, done){
            animating=true;
            withTransition(ms);
            scale=ns; tx=nx; ty=ny; bg=nbg;
            paint();
            setTimeout(function(){ noTransition(); animating=false; if(done) done(); }, ms+20);
          }

          function computeBase(nw, nh){
            if(!nw || !nh){ var r=vImg.getBoundingClientRect(); nw=r.width||1; nh=r.height||1; }
            var vw=window.innerWidth, vh=window.innerHeight;
            var f=Math.min(vw/nw, vh/nh);
            if(f>6) f=6;
            baseW=nw*f; baseH=nh*f;
            baseX=(vw-baseW)/2; baseY=(vh-baseH)/2;
          }
          function boundsX(){
            var vw=window.innerWidth, sw=baseW*scale;
            if(sw<=vw){ var c=(baseW-sw)/2; return [c,c]; }
            return [vw-baseX-sw, -baseX];
          }
          function boundsY(){
            var vh=window.innerHeight, sh=baseH*scale;
            if(sh<=vh){ var c=(baseH-sh)/2; return [c,c]; }
            return [vh-baseY-sh, -baseY];
          }
          function clampPan(){
            var bx=boundsX(), by=boundsY();
            tx=Math.min(Math.max(tx,bx[0]),bx[1]);
            ty=Math.min(Math.max(ty,by[0]),by[1]);
          }
          function rubber(v, lo, hi){
            if(v<lo) return lo-(lo-v)*RUBBER;
            if(v>hi) return hi+(v-hi)*RUBBER;
            return v;
          }
          // scale around a screen point, keeping it pinned under the fingers/tap
          function focalZoom(ns, fx, fy, s0, tx0, ty0){
            var lx=(fx-(baseX+tx0))/s0, ly=(fy-(baseY+ty0))/s0;
            scale=ns;
            tx=fx-baseX-lx*ns;
            ty=fy-baseY-ly*ns;
          }

          function open(el){
            srcEl=el;
            var nw=el.naturalWidth||el.width, nh=el.naturalHeight||el.height;
            computeBase(nw, nh);
            vImg.src=el.src;
            vImg.style.width=baseW+'px';
            vImg.style.height=baseH+'px';
            vImg.style.opacity='1';

            var r=el.getBoundingClientRect();
            scale=r.width/baseW || 0.01;
            tx=r.left-baseX;
            ty=r.top-baseY;
            bg=0;
            noTransition(); paint();
            iv.style.display='block';
            iv.getBoundingClientRect();          // reflow so the transition has a start value

            state='open';
            nav('swipe://disable');
            animateTo(1, 0, 0, 1, OPEN_MS, function(){
              var mid = (srcEl && srcEl.getAttribute('data-media-id')) || '';
              var pg  = srcEl && srcEl.getAttribute('data-page');
              if(pg) mid = mid + ':' + pg;
              if(mid) nav('img-viewer://open/' + encodeURIComponent(mid));
            });
          }

          function finishClose(){
            iv.style.display='none';
            vImg.src=''; vImg.style.opacity='1';
            scale=1; tx=0; ty=0; bg=1;
            noTransition(); paint();
            srcEl=null;
            nav('swipe://enable');
          }
          function close(){
            if(state!=='open') return;
            state='closed';
            var vw=window.innerWidth, vh=window.innerHeight;
            var r=srcEl?srcEl.getBoundingClientRect():null;
            var visible=r && r.width>0 && r.bottom>0 && r.top<vh && r.right>0 && r.left<vw;
            if(visible){
              animateTo(r.width/baseW, r.left-baseX, r.top-baseY, 0, OPEN_MS, finishClose);
            } else {
              animating=true;
              vImg.style.transition='transform '+OPEN_MS+'ms cubic-bezier(.2,0,0,1),opacity '+OPEN_MS+'ms linear';
              iv.style.transition='background-color '+OPEN_MS+'ms linear';
              scale=Math.max(scale*0.9, 0.05); bg=0;
              vImg.style.opacity='0';
              paint();
              setTimeout(function(){ animating=false; finishClose(); }, OPEN_MS+20);
            }
          }

          window._setViewerFullRes = function(src){
            if(state!=='open') return;
            var im=new Image();
            im.onload=function(){ if(state==='open') vImg.src=src; };
            im.onerror=function(){};
            im.src=src;
          };

          document.addEventListener('click', function(e){
            if(state==='open') return;
            var t=e.target;
            if(t && t.tagName==='IMG' && t.id!=='_iv_img' && t.src){
              e.preventDefault();
              open(t);
            }
          });

          document.addEventListener('contextmenu', function(e){
            if(e.target.tagName==='IMG' && e.target.id!=='_iv_img'){
              var mid=e.target.getAttribute('data-media-id');
              if(mid){ e.preventDefault(); nav('img-download://'+encodeURIComponent(mid)); }
            }
          });

          window.addEventListener('resize', function(){
            if(state!=='open' || animating) return;
            computeBase(vImg.naturalWidth, vImg.naturalHeight);
            vImg.style.width=baseW+'px'; vImg.style.height=baseH+'px';
            clampPan(); paint();
          });

          function d2(a,b){ return Math.hypot(a.clientX-b.clientX, a.clientY-b.clientY); }
          function mid2(a,b){ return {x:(a.clientX+b.clientX)/2, y:(a.clientY+b.clientY)/2}; }

          var g=null;                          // active gesture
          var lastTapT=0, lastTapX=0, lastTapY=0, singleTapTimer=0;
          function cancelSingleTap(){ if(singleTapTimer){ clearTimeout(singleTapTimer); singleTapTimer=0; } }

          var ZOOM_SNAP=1.15;              // releasing below this returns to a clean fit
          // single place a zoom settles after a pinch or a pan: snap the near-fit band back to
          // 1, cap the top, clamp pan for the resulting scale, then animate there from where the
          // fingers actually left off
          function settleZoom(){
            var s=scale, X=tx, Y=ty;
            var ns=scale, nx=tx, ny=ty;
            if(scale<ZOOM_SNAP){ ns=1; nx=0; ny=0; }
            else if(scale>MAX_SCALE){ ns=MAX_SCALE; }
            scale=ns; tx=nx; ty=ny; clampPan();
            nx=tx; ny=ty;
            scale=s; tx=X; ty=Y;
            if(ns!==s || nx!==X || ny!==Y) animateTo(ns, nx, ny, 1, SPRING_MS);
          }

          iv.addEventListener('touchstart', function(e){
            if(state!=='open' || animating) return;
            cancelSingleTap();
            if(e.touches.length===1){
              var t=e.touches[0];
              g={ mode:(scale>1.01?'pan':'dismiss'),
                  x0:t.clientX, y0:t.clientY, tx0:tx, ty0:ty, moved:false,
                  vt:performance.now(), vy:0, py:t.clientY };
            } else if(e.touches.length===2){
              var a=e.touches[0], b=e.touches[1], m=mid2(a,b);
              g={ mode:'pinch', d0:d2(a,b)||1, s0:scale, fx:m.x, fy:m.y,
                  tx0:tx, ty0:ty, moved:true };
            }
          }, {passive:false});

          iv.addEventListener('touchmove', function(e){
            if(!g || state!=='open' || animating) return;
            e.preventDefault();

            if(g.mode==='pinch' && e.touches.length>=2){
              var a=e.touches[0], b=e.touches[1];
              var raw=g.s0*d2(a,b)/g.d0;
              focalZoom(rubber(raw, 1, MAX_SCALE), g.fx, g.fy, g.s0, g.tx0, g.ty0);
              schedule();
              return;
            }
            if(e.touches.length!==1) return;

            var t=e.touches[0];
            var dx=t.clientX-g.x0, dy=t.clientY-g.y0;
            if(!g.moved && Math.hypot(dx,dy)>TAP_SLOP) g.moved=true;
            if(!g.moved) return;

            var now=performance.now(), dt=now-g.vt;
            if(dt>0){ g.vy=(t.clientY-g.py)/dt; g.vt=now; g.py=t.clientY; }

            if(g.mode==='pan'){
              var bx=boundsX(), by=boundsY();
              tx=rubber(g.tx0+dx, bx[0], bx[1]);
              ty=rubber(g.ty0+dy, by[0], by[1]);
              schedule();
            } else {
              tx=dx*0.5; ty=dy;
              scale=1 - Math.min(Math.abs(dy)/DISMISS_SHRINK, 1)*0.25;
              bg=1 - Math.min(Math.abs(dy)/DISMISS_FADE, 1)*0.9;
              schedule();
            }
          }, {passive:false});

          iv.addEventListener('touchend', function(e){
            if(!g || state!=='open') return;
            var mode=g.mode, moved=g.moved, vy=g.vy||0, endTy=ty;

            if(e.touches.length>0){
              if(mode==='pinch'){
                var t=e.touches[0];
                g={ mode:'pan', x0:t.clientX, y0:t.clientY, tx0:tx, ty0:ty, moved:true,
                    vt:performance.now(), vy:0, py:t.clientY };
              }
              return;
            }
            g=null;
            if(animating) return;

            if(mode==='dismiss'){
              if(!moved){ handleTap(e); return; }
              if(Math.abs(endTy)>DISMISS_SHIFT || Math.abs(vy)>DISMISS_VEL) close();
              else animateTo(1, 0, 0, 1, SPRING_MS);
              return;
            }
            // pinch or pan
            if(!moved){ handleTap(e); return; }
            settleZoom();
          }, {passive:false});

          function handleTap(e){
            var now=performance.now();
            var ct=e.changedTouches && e.changedTouches[0];
            var x=ct?ct.clientX:0, y=ct?ct.clientY:0;
            if(now-lastTapT<DBL_MS && Math.hypot(x-lastTapX, y-lastTapY)<DBL_SLOP){
              lastTapT=0; cancelSingleTap();
              doubleTap(x, y);
              return;
            }
            lastTapT=now; lastTapX=x; lastTapY=y;
            if(scale>1.01) return;              // zoomed: reserve the tap for a double
            singleTapTimer=setTimeout(function(){ singleTapTimer=0; close(); }, DBL_MS);
          }

          function doubleTap(x, y){
            if(scale>1.01){
              animateTo(1, 0, 0, 1, SPRING_MS);
              return;
            }
            var s0=scale, x0=tx, y0=ty;
            focalZoom(DBL_ZOOM, x, y, s0, x0, y0);
            clampPan();
            var nx=tx, ny=ty;
            scale=s0; tx=x0; ty=y0;
            animateTo(DBL_ZOOM, nx, ny, 1, SPRING_MS);
          }
        })();
        </script>
        """;
#endif
}
