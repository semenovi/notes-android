namespace Notes.Helpers;

public static class ImageViewerHtml
{
#if WINDOWS
    public const string ViewerCss = "img { cursor: zoom-in; }";
    public const string ViewerDiv = "";
    // Encode the element ID (or fallback src URL) directly in the navigation URL —
    // sessionStorage is unreliable for file:// origins in WebView2.
    public const string ViewerScript = """
        <script>
        document.addEventListener('click', function(e) {
          if (e.target.tagName !== 'IMG' || !e.target.src) return;
          var id  = e.target.id  || '';
          var src = e.target.src || '';
          // For media images use the short element ID; for others use the src URL
          // (skip data: URIs for non-media images — they are too large for a URL).
          var payload = id.startsWith('media-') ? id
                      : src.startsWith('data:')  ? ''
                      : src;
          if (!payload) return;
          window.location.href = 'img-viewer://open/' + encodeURIComponent(payload);
        });
        </script>
        """;
#else
    // CSS is intentionally single-line: the C# preprocessor scans skipped #if/#else branches
    // for directives and would mistake CSS "#_iv {" on its own line for a preprocessor directive.
    public const string ViewerCss =
        "img:not(#_iv_img){cursor:pointer;}" +
        " #_iv{display:none;position:fixed;top:0;left:0;width:100%;height:100%;" +
            "background:rgba(0,0,0,0.92);z-index:9999;overflow:hidden;}" +
        " #_iv_img{position:absolute;top:50%;left:50%;transform:translate(-50%,-50%);" +
            "max-width:95vw;max-height:95vh;border-radius:4px;touch-action:none;" +
            "user-select:none;will-change:transform;}";

    public const string ViewerDiv = "<div id='_iv'><img id='_iv_img'/></div>";

    public const string ViewerScript = """
        <script>
        (function(){
          var iv   = document.getElementById('_iv');
          var vImg = document.getElementById('_iv_img');
          var sc=1, ld=0, tx=0, ty=0;
          var drag=false, pinch=false;
          var lx=0, ly=0, sx=0, sy=0;

          function xf() {
            return 'translate(calc(-50% + '+tx+'px), calc(-50% + '+ty+'px)) scale('+sc+')';
          }
          function show(src) {
            vImg.src=src; sc=1; tx=0; ty=0; drag=false; pinch=false;
            vImg.style.cssText='position:absolute;top:50%;left:50%;' +
              'transform:translate(-50%,-50%) scale(1);' +
              'max-width:95vw;max-height:95vh;border-radius:4px;' +
              'touch-action:none;user-select:none;will-change:transform;opacity:1;';
            iv.style.cssText='display:block;position:fixed;top:0;left:0;' +
              'width:100%;height:100%;background:rgba(0,0,0,0.92);z-index:9999;overflow:hidden;';
          }
          function hide() {
            iv.style.display='none'; vImg.src=''; sc=1; tx=0; ty=0; drag=false; pinch=false;
          }

          document.addEventListener('click', function(e){
            if(e.target.tagName==='IMG' && e.target.id!=='_iv_img' && e.target.src)
              show(e.target.src);
          });

          // Single finger: pan when zoomed, or swipe-to-dismiss when at scale≈1
          iv.addEventListener('touchstart', function(e){
            if(e.touches.length===1 && !pinch){
              lx=sx=e.touches[0].clientX;
              ly=sy=e.touches[0].clientY;
              drag=true;
            }
          }, {passive:true});

          iv.addEventListener('touchmove', function(e){
            if(!drag || e.touches.length!==1) return;
            var cx=e.touches[0].clientX, cy=e.touches[0].clientY;
            tx+=cx-lx; ty+=cy-ly;
            lx=cx; ly=cy;
            vImg.style.transition='none';
            vImg.style.transform=xf();
            if(sc<1.1){
              iv.style.transition='none';
              iv.style.background='rgba(0,0,0,'+Math.max(0.05,0.92-Math.abs(cy-sy)/250)+')';
            }
          }, {passive:true});

          iv.addEventListener('touchend', function(e){
            if(!drag) return; drag=false;
            var ey=e.changedTouches.length>0?e.changedTouches[0].clientY:ly;
            var totalDy=ey-sy;
            if(sc<1.1 && Math.abs(totalDy)>80){
              // Dismiss
              var dir=totalDy>0?1:-1;
              vImg.style.transition='transform 0.22s ease-out,opacity 0.18s ease-out';
              vImg.style.transform='translate(calc(-50% + '+tx+'px),calc(-50% + '+(ty+dir*(window.innerHeight+100))+'px)) scale('+sc+')';
              vImg.style.opacity='0';
              iv.style.transition='background 0.22s ease-out';
              iv.style.background='rgba(0,0,0,0)';
              setTimeout(hide, 220);
            } else if(sc<1.1){
              // Snap back to center
              tx=0; ty=0;
              vImg.style.transition='transform 0.22s ease-out';
              iv.style.transition='background 0.22s ease-out';
              vImg.style.transform='translate(-50%,-50%) scale('+sc+')';
              iv.style.background='rgba(0,0,0,0.92)';
              setTimeout(function(){vImg.style.transition='';iv.style.transition='';}, 220);
            }
            // When zoomed (sc≥1.1): stay at panned position
          }, {passive:true});

          // Pinch-to-zoom
          vImg.addEventListener('touchstart', function(e){
            if(e.touches.length===2){
              pinch=true; drag=false;
              ld=Math.hypot(e.touches[0].clientX-e.touches[1].clientX,
                            e.touches[0].clientY-e.touches[1].clientY);
              e.preventDefault();
            }
          }, {passive:false});

          vImg.addEventListener('touchmove', function(e){
            if(e.touches.length===2){
              var d=Math.hypot(e.touches[0].clientX-e.touches[1].clientX,
                               e.touches[0].clientY-e.touches[1].clientY);
              sc=Math.min(Math.max(sc*d/ld,0.5),5);
              ld=d;
              vImg.style.transform=xf();
              e.preventDefault();
            }
          }, {passive:false});

          vImg.addEventListener('touchend', function(e){
            if(e.touches.length<2){
              pinch=false;
              // Snap tx/ty back to center when pinched back to normal
              if(sc<1.05){ sc=1; tx=0; ty=0; vImg.style.transform=xf(); }
            }
          }, {passive:true});
        })();
        </script>
        """;
#endif
}
