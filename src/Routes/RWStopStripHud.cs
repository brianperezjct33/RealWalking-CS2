// Real Walking transit stop-strip HUD (Unity IMGUI).
// Rendering only: never queries or writes game simulation entities.
// Route/stop data is collected by RWStopStripSystem in RWStopStrip.cs.
// The ring can be dense; full line and next-stop names appear in the header,
// while densely packed node captions may be abbreviated without ellipses.
using System;
using UnityEngine;
using Color = UnityEngine.Color;

namespace CS2RealWalkingG1Bridge
{
    // No GL or rotated GUI matrices for line segments: all segments are rasterized
    // into ONE cached transparent texture. This avoids the broken/disconnected
    // green strokes seen in S1.6 when GUIUtility.RotateAroundPivot was nested
    // under the global 1.5x GUI.matrix scaling.
    internal sealed class RWStopStripHud : MonoBehaviour
    {
        private static RWStopStripHud _instance;
        private GUIStyle _title, _stop, _small, _left;
        private Texture2D _routeTexture;
        // Calculated only when the route snapshot changes (normally every 0.5 s).
        private RWStopStripSnapshot? _captionsFor;
        private string?[] _mapCaptions=Array.Empty<string?>();
        private int _cachedW=-1,_cachedCount=-1,_cachedCurrent=-999,_cachedNext=-999,_cachedStart=-999;
        private Color32 _cachedTint;
        private const int RouteHeight=165;
        private const float UiScale=2.0f;
        private const int MaxVisibleStations=28;
        internal static void Ensure()
        {
            if(_instance!=null)return;
            var go=new GameObject("RW_Transit_StopStrip");
            go.hideFlags=HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _instance=go.AddComponent<RWStopStripHud>();
        }
        internal static void Release()
        {
            RWStopStripState.Clear();
            if(_instance!=null)Destroy(_instance.gameObject);
            _instance=null;
        }
        private void OnDestroy()
        {
            if(_routeTexture!=null){Destroy(_routeTexture);_routeTexture=null;}
            if(_instance==this)_instance=null;
        }
        private static Vector2 Point(float fraction,float left,float right,float cy,float radius)
        {
            float straight=Mathf.Max(1f,right-left-2f*radius);
            float circumference=2f*straight+2f*Mathf.PI*radius;
            float distance=Mathf.Repeat(fraction,1f)*circumference;
            float arc=Mathf.PI*.5f*radius;
            // Start at the leftmost point, move clockwise along top -> right ->
            // bottom -> left; the last pixel joins the first pixel exactly.
            if(distance<arc)
            {
                float a=Mathf.PI+distance/radius;
                return new Vector2(left+radius+radius*Mathf.Cos(a),cy+radius*Mathf.Sin(a));
            }
            distance-=arc;
            if(distance<straight)return new Vector2(left+radius+distance,cy-radius);
            distance-=straight;
            if(distance<Mathf.PI*radius)
            {
                float a=-.5f*Mathf.PI+distance/radius;
                return new Vector2(right-radius+radius*Mathf.Cos(a),cy+radius*Mathf.Sin(a));
            }
            distance-=Mathf.PI*radius;
            if(distance<straight)return new Vector2(right-radius-distance,cy+radius);
            distance-=straight;
            float angle=.5f*Mathf.PI+distance/radius;
            return new Vector2(left+radius+radius*Mathf.Cos(angle),cy+radius*Mathf.Sin(angle));
        }
        private static void Pixel(Color32[] pixels,int w,int h,int x,int y,Color32 ink,float coverage)
        {
            if(x<0||x>=w||y<0||y>=h||coverage<=0f)return;
            int i=(h-1-y)*w+x;
            float opacity=ink.a/255f*Mathf.Clamp01(coverage);
            float a=pixels[i].a/255f;
            float outA=opacity+a*(1f-opacity);
            if(outA<=0.0001f)return;
            float r=(ink.r*opacity+pixels[i].r*a*(1f-opacity))/outA;
            float g=(ink.g*opacity+pixels[i].g*a*(1f-opacity))/outA;
            float b=(ink.b*opacity+pixels[i].b*a*(1f-opacity))/outA;
            pixels[i]=new Color32((byte)r,(byte)g,(byte)b,(byte)(outA*255f));
        }
        private static void Stroke(Color32[] pixels,int w,int h,Vector2 a,Vector2 b,float thickness,Color32 ink)
        {
            float radius=thickness*.5f;
            int minX=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.x,b.x)-radius-1f));
            int maxX=Mathf.Min(w-1,Mathf.CeilToInt(Mathf.Max(a.x,b.x)+radius+1f));
            int minY=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.y,b.y)-radius-1f));
            int maxY=Mathf.Min(h-1,Mathf.CeilToInt(Mathf.Max(a.y,b.y)+radius+1f));
            Vector2 delta=b-a;float denom=delta.sqrMagnitude;
            for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
            {
                Vector2 p=new Vector2(x+.5f,y+.5f);
                float t=denom<.00001f?0f:Mathf.Clamp01(Vector2.Dot(p-a,delta)/denom);
                float dist=(p-(a+t*delta)).magnitude;
                Pixel(pixels,w,h,x,y,ink,Mathf.Clamp01(radius+.65f-dist));
            }
        }
        private static void Circle(Color32[] pixels,int w,int h,Vector2 p,float radius,Color32 ink)
        {
            int minX=Mathf.Max(0,Mathf.FloorToInt(p.x-radius-1f));
            int maxX=Mathf.Min(w-1,Mathf.CeilToInt(p.x+radius+1f));
            int minY=Mathf.Max(0,Mathf.FloorToInt(p.y-radius-1f));
            int maxY=Mathf.Min(h-1,Mathf.CeilToInt(p.y+radius+1f));
            for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
            {
                float dist=Vector2.Distance(new Vector2(x+.5f,y+.5f),p);
                Pixel(pixels,w,h,x,y,ink,Mathf.Clamp01(radius+.65f-dist));
            }
        }
        private static Color32 Ink(Color c){return (Color32)c;}
        private void RefreshTexture(RWStopStripSnapshot state,int width,int count,int start)
        {
            Color32 tint=Ink(state.Tint);
            if(_routeTexture!=null && _cachedW==width && _cachedCount==count &&
                _cachedCurrent==state.Current && _cachedNext==state.Next &&
                _cachedStart==start &&
                _cachedTint.Equals(tint))return;
            if(_routeTexture!=null){Destroy(_routeTexture);_routeTexture=null;}
            var pixels=new Color32[width*RouteHeight];
            float left=26f,right=width-26f,cy=93f,r=35f;
            // continuous track from one texture; anti-aliased, and no rotated IMGUI.
            const int resolution=220;
            Vector2 prev=Point(0f,left,right,cy,r);
            for(int i=1;i<=resolution;i++)
            {
                Vector2 next=Point((float)i/resolution,left,right,cy,r);
                Stroke(pixels,width,RouteHeight,prev,next,4.2f,tint);
                prev=next;
            }
            int total=state.Stops.Length;
            for(int j=0;j<count;j++)
            {
                int idx=total>count?(start+j)%total:j;
                Vector2 p=Point((float)j/count,left,right,cy,r);
                bool current=idx==state.Current,upcoming=idx==state.Next,origin=idx==0;
                float rad=current?7.5f:upcoming?6.8f:origin?6f:5.3f;
                Color32 dot=Ink(current?new Color(1f,.33f,.29f,1f):
                    upcoming?new Color(1f,.86f,.16f,1f):Color.white);
                Circle(pixels,width,RouteHeight,p,rad+2.6f,Ink(new Color(.03f,.07f,.10f,.98f)));
                Circle(pixels,width,RouteHeight,p,rad,dot);
            }
            _routeTexture=new Texture2D(width,RouteHeight,TextureFormat.RGBA32,false);
            _routeTexture.name="RW_Continuous_Capsule_Route";
            _routeTexture.filterMode=FilterMode.Bilinear;
            _routeTexture.wrapMode=TextureWrapMode.Clamp;
            _routeTexture.SetPixels32(pixels);
            _routeTexture.Apply(false,true);
            _cachedW=width;_cachedCount=count;_cachedCurrent=state.Current;
            _cachedNext=state.Next;_cachedTint=tint;_cachedStart=start;
        }
        private void EnsureStyles()
        {
            if(_title!=null)return;
            // The line and next-stop headers now wrap instead of truncating
            // translated / custom names to a fixed number of characters.
            _title=new GUIStyle(GUI.skin.label){fontSize=16,fontStyle=FontStyle.Bold,
                alignment=TextAnchor.UpperLeft,wordWrap=true};
            _title.normal.textColor=Color.white;
            _stop=new GUIStyle(GUI.skin.label){fontSize=12,fontStyle=FontStyle.Bold,
                alignment=TextAnchor.UpperCenter,wordWrap=true};
            _stop.normal.textColor=Color.white;
            _small=new GUIStyle(GUI.skin.label){fontSize=12,
                alignment=TextAnchor.UpperRight,wordWrap=true};
            _small.normal.textColor=Color.white;
            _left=new GUIStyle(_small){alignment=TextAnchor.MiddleLeft};
        }
        // Dense schematic dots have a finite caption footprint. Preserve the
        // full name in the header when upcoming; for other map dots prefer
        // full two-line names, then readable word initials rather than a
        // misleading character prefix plus ellipsis.
        private string MapCaption(string fullName)
        {
            if(string.IsNullOrWhiteSpace(fullName))return "";
            const float labelWidth=103f;
            const float availableHeight=38f;
            if(_stop.CalcHeight(new GUIContent(fullName),labelWidth)<=availableHeight)
                return fullName;
            string[] words=fullName.Split(new[]{' ','\t','-','_','/','\n'},
                StringSplitOptions.RemoveEmptyEntries);
            if(words.Length>=2)
            {
                var abbreviation=new System.Text.StringBuilder(words.Length*2);
                for(int i=0;i<words.Length;i++)
                {
                    if(i>0 && i%6==0)abbreviation.Append('\n');
                    abbreviation.Append(words[i][0]);
                }
                return abbreviation.ToString();
            }
            // Chinese/Japanese/Korean names typically wrap correctly without
            // spaces. Keep the source name rather than destroying its meaning.
            return fullName;
        }
        private string CachedMapCaption(RWStopStripSnapshot state,int index)
        {
            if(!ReferenceEquals(_captionsFor,state))
            {
                _captionsFor=state;
                _mapCaptions=new string?[state.Stops.Length];
            }
            if(_mapCaptions[index]==null)
                _mapCaptions[index]=MapCaption(state.Stops[index]);
            return _mapCaptions[index] ?? "";
        }
        // Reuse each GUIStyle; reduce its font only when a wrapped header would
        // exceed the available text area. Never mutate the underlying name.
        private static void WrappedHeader(Rect rect,string text,GUIStyle style,int minFontSize)
        {
            int original=style.fontSize;
            var content=new GUIContent(text??"");
            try
            {
                while(style.fontSize>minFontSize && style.CalcHeight(content,rect.width)>rect.height)
                    style.fontSize--;
                Caption(rect,text,style);
            }
            finally { style.fontSize=original; }
        }
        private static void RectColor(Rect rect,Color color)
        {
            Color old=GUI.color;GUI.color=color;
            GUI.DrawTexture(rect,Texture2D.whiteTexture);
            GUI.color=old;
        }
        private static void Caption(Rect rect,string name,GUIStyle style)
        {
            // Small dark shadow keeps text legible over bright stations while the
            // panel itself remains mostly transparent.
            // No per-frame GUIStyle allocation: stations refresh at 60+ FPS.
            Color original=style.normal.textColor;
            style.normal.textColor=new Color(0f,0f,0f,.8f);
            try {GUI.Label(new Rect(rect.x+1f,rect.y+1f,rect.width,rect.height),name,style);}
            finally {style.normal.textColor=original;}
            GUI.Label(rect,name,style);
        }
        private void OnGUI()
        {
            if(UnityEngine.Event.current.type!=UnityEngine.EventType.Repaint)return;
            RWStopStripSnapshot state=RWStopStripState.Latest;
            if(state==null||!RWTransitRide.Active||state.Stops.Length==0)return;
            EnsureStyles();
            Matrix4x4 old=GUI.matrix;
            try
            {
                GUI.matrix=Matrix4x4.Scale(new Vector3(UiScale,UiScale,1f));
                float vw=Screen.width/UiScale,vh=Screen.height/UiScale;
                int width=Mathf.FloorToInt(Mathf.Min(vw-16f,950f));
                if(width<260||vh<RouteHeight+20)return;
                float x=(vw-width)*.5f,y=vh-RouteHeight-9f;
                // translucent glass HUD rather than S1.6's 81%-opaque black box.
                RectColor(new Rect(x,y,width,RouteHeight),new Color(.035f,.07f,.105f,.23f));
                RectColor(new Rect(x,y,width,1f),new Color(1f,1f,1f,.22f));
                RectColor(new Rect(x,y+RouteHeight-1,width,1f),new Color(1f,1f,1f,.17f));
                int total=state.Stops.Length,count=Mathf.Min(total,MaxVisibleStations);
                int start=total>count&&state.Next>=0?(state.Next-5+total)%total:0;
                RefreshTexture(state,width,count,start);
                if(_routeTexture!=null)GUI.DrawTexture(new Rect(x,y,width,RouteHeight),_routeTexture,ScaleMode.StretchToFill,true);
                string next=(state.Next>=0 && state.Next<total)?RWLocalization.T("rw.stop.next",state.Stops[state.Next]):"";
                // The header uses real GUI wrapping. Two columns are used on
                // wide screens; smaller screens stack line and next stop.
                if(width>=620)
                {
                    WrappedHeader(new Rect(x+18f,y+4f,width*.48f,48f),state.Line,_title,11);
                    WrappedHeader(new Rect(x+width*.52f,y+5f,width*.45f,46f),next,_small,10);
                }
                else
                {
                    WrappedHeader(new Rect(x+12f,y+2f,width-24f,22f),state.Line,_title,11);
                    WrappedHeader(new Rect(x+12f,y+24f,width-24f,33f),next,_small,10);
                }
                // The station dots are all in the continuous texture; the labels
                // are ordinary GUI text and never cut or bend the route stroke.
                int labelEvery=Mathf.Max(1,Mathf.CeilToInt(count/12f));
                for(int j=0;j<count;j++)
                {
                    int i=total>count?(start+j)%total:j;
                    bool active=(i==state.Next || i==state.Current || i==0);
                    if(j%labelEvery!=0&&!active)continue;
                    Vector2 p=Point((float)j/count,26f,width-26f,93f,35f);
                    string label=CachedMapCaption(state,i);
                    if(i==0)label=RWLocalization.T("rw.stop.origin",label);
                    float lx=Mathf.Clamp(p.x-51f,4f,width-106f);
                    float ly=p.y<93f?p.y+13f:p.y-38f;
                    if(Mathf.Abs(p.y-93f)<8f)ly=p.y+13f;
                    // Keep labels clear of the count footer at the bottom.
                    ly=Mathf.Clamp(ly,56f,RouteHeight-62f);
                    Caption(new Rect(x+lx,y+ly,103f,38f),label,_stop);
                }
                string footer=total>count?RWLocalization.T("rw.stop.partial",count,total):RWLocalization.T("rw.stop.total",total);
                Caption(new Rect(x+15f,y+RouteHeight-23f,width-30f,20f),footer,_left);
            }
            finally{GUI.matrix=old;}
        }
    }
}
