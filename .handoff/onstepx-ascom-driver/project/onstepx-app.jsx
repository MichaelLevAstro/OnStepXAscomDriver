// OnStepX main app — renders both windows and wires Tweaks.
const { useState, useEffect } = React;

const TWEAK_DEFAULTS = /*EDITMODE-BEGIN*/{
  "theme": "dark",
  "accentHue": 25,
  "accentChroma": 0.22,
  "accentLightness": 0.62,
  "radius": 6,
  "density": "comfortable",
  "fontFamily": "Inter",
  "monoFamily": "JetBrains Mono",
  "consoleHeight": 220,
  "sectionIcons": true,
  "statusPulse": true,
  "showPopup": true
}/*EDITMODE-END*/;

const FONT_STACKS = {
  "Inter": `"Inter", "Segoe UI", system-ui, sans-serif`,
  "Segoe UI": `"Segoe UI", system-ui, sans-serif`,
  "IBM Plex Sans": `"IBM Plex Sans", system-ui, sans-serif`,
  "Helvetica": `"Helvetica Neue", Helvetica, Arial, sans-serif`,
  "System": `system-ui, -apple-system, sans-serif`,
};
const MONO_STACKS = {
  "JetBrains Mono": `"JetBrains Mono", ui-monospace, monospace`,
  "IBM Plex Mono":  `"IBM Plex Mono", ui-monospace, monospace`,
  "Consolas":       `Consolas, "Courier New", monospace`,
  "Menlo":          `Menlo, ui-monospace, monospace`,
};

const DENSITY = {
  compact:     { gap: 6,  pad: 10, row: 26, header: 32 },
  comfortable: { gap: 10, pad: 12, row: 28, header: 38 },
  spacious:    { gap: 14, pad: 16, row: 32, header: 42 },
};

// ── chrome helpers ─────────────────────────────────────────────
const Chev = () => (
  <svg width="12" height="12" viewBox="0 0 12 12"><path d="M2 4l4 4 4-4" stroke="currentColor" fill="none" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round"/></svg>
);

function Section({ icon, title, defaultOpen = true, headerRight, children, showIcon }) {
  const [open, setOpen] = useState(defaultOpen);
  return (
    <div className={"section" + (open ? "" : " collapsed")}>
      <div className="section-header" onClick={() => setOpen(o => !o)}>
        {showIcon && <span className="icon">{icon}</span>}
        <div className="title">{title}</div>
        {headerRight && <div style={{marginLeft:"auto", marginRight:8}} onClick={(e)=>e.stopPropagation()}>{headerRight}</div>}
        <span className="chev" style={headerRight ? {} : {marginLeft:"auto"}}><Chev/></span>
      </div>
      <div className="section-body">{children}</div>
    </div>
  );
}

function Check({ checked, children, onChange }) {
  return (
    <label className="check">
      <input type="checkbox" checked={!!checked} onChange={(e)=>onChange && onChange(e.target.checked)} />
      <span className="box"></span>
      <span>{children}</span>
    </label>
  );
}

function Row({ label, children, width = 92 }) {
  return (
    <div className="row" style={{gridTemplateColumns: `${width}px 1fr`}}>
      <div className="label">{label}</div>
      <div className="controls">{children}</div>
    </div>
  );
}

// ── icons per section ──────────────────────────────────────────
const IC = {
  conn: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round"><path d="M5 12a7 7 0 0 1 14 0"/><path d="M8.5 12a3.5 3.5 0 0 1 7 0"/><circle cx="12" cy="12" r="1.2" fill="currentColor"/><path d="M12 13v6"/></svg>,
  site: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18"/></svg>,
  clock:<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>,
  track:<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="4"/><path d="M12 3v3M12 18v3M3 12h3M18 12h3"/></svg>,
  lim:  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M3 19h18"/><path d="M6 19V9M12 19V5M18 19v-7"/></svg>,
  pos:  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 3v18M3 12h18"/><circle cx="12" cy="12" r="2.5" fill="currentColor"/></svg>,
  park: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M3 10l9-6 9 6"/><path d="M5 10v10h14V10"/><path d="M10 20v-6h4v6"/></svg>,
  slew: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M12 3v18M3 12h18"/><path d="M12 3l-3 3M12 3l3 3M12 21l-3-3M12 21l3-3M3 12l3-3M3 12l3 3M21 12l-3-3M21 12l-3 3"/></svg>,
  cons: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M4 6l4 4-4 4"/><path d="M12 16h8"/></svg>,
  pin:  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M12 22s-7-6.5-7-12a7 7 0 0 1 14 0c0 5.5-7 12-7 12z"/><circle cx="12" cy="10" r="2.4"/></svg>,
  tele: <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M3 14l8-3 3 8-8 3z"/><path d="M11 11l8-3"/><path d="M15 3l3 8"/><path d="M9 20l2-1"/><path d="M13 22l2-4"/></svg>,
};

const LOG_LINES = [
  ["16:11:30.210", ":Qn#",  "(blind)",         "",       true],
  ["16:11:30.210", ":Qw#",  "(blind)",         "",       true],
  ["16:11:30.241", ":GDH#", "+65°57:20.891#",  "32 ms",  false],
  ["16:11:30.259", ":GA#",  "+40°07:33#",      "31 ms",  false],
  ["16:11:30.288", ":GZ#",  "028°07:56#",      "16 ms",  false],
  ["16:11:30.320", ":GS#",  "05:40:59#",       "31 ms",  false],
  ["16:11:30.331", ":Gm#",  "W#",              "15 ms",  false],
  ["16:11:30.351", ":GU#",  "nNPeA/EW190#",    "16 ms",  false],
  ["16:11:30.383", ":GT#",  "0#",              "31 ms",  false],
];

function MainWindow({ t, setTweak }) {
  const d = DENSITY[t.density] || DENSITY.comfortable;

  return (
    <div className="window" data-screen-label="01 Main">
      <div className="titlebar">
        <div className="logo">{IC.tele}</div>
        <div className="title">OnStepX ASCOM Driver <span className="dim">· v2.4.1</span></div>
        <div className="spacer"></div>

        <div className="theme-toggle">
          <button className={t.theme==="dark" ? "active":""} title="Dark" onClick={()=>setTweak("theme","dark")}>
            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg>
          </button>
          <button className={t.theme==="light" ? "active":""} title="Light" onClick={()=>setTweak("theme","light")}>
            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>
          </button>
        </div>

        <button className="win-btn" title="Minimize"><svg width="10" height="10" viewBox="0 0 10 10"><path d="M1 5h8" stroke="currentColor" strokeWidth="1.2"/></svg></button>
        <button className="win-btn" title="Maximize"><svg width="10" height="10" viewBox="0 0 10 10"><rect x="1.5" y="1.5" width="7" height="7" fill="none" stroke="currentColor" strokeWidth="1.2"/></svg></button>
        <button className="win-btn close" title="Close"><svg width="10" height="10" viewBox="0 0 10 10"><path d="M1 1l8 8M9 1l-8 8" stroke="currentColor" strokeWidth="1.2"/></svg></button>
      </div>

      <div className="body">
        {/* LEFT PANE */}
        <div className="pane left scroll">
          <Section icon={IC.conn} title="Connection" showIcon={t.sectionIcons}>
            <Row label="Transport"><select className="select" style={{width:100}}><option>Serial</option><option>TCP</option></select></Row>
            <Row label="COM port">
              <select className="select" style={{width:110}}><option>COM3</option></select>
              <div className="label" style={{marginLeft:8}}>Baud</div>
              <select className="select" style={{width:92}}><option>9600</option></select>
            </Row>
            <Row label="TCP host / port">
              <input className="input" defaultValue="192.168.0.1" />
              <input className="num" defaultValue="9999" />
            </Row>
            <div className="conn-row">
              <div className={"status ok" + (t.statusPulse?"":" no-pulse")}><span className="dot"></span>Connected</div>
              <button className="btn sm">Auto-Detect</button>
              <button className="btn sm" disabled>Connect</button>
              <button className="btn sm primary">Disconnect</button>
            </div>
            <Check checked={true}>Auto-connect to saved port on open</Check>
          </Section>

          <Section icon={IC.site} title="Site" showIcon={t.sectionIcons}>
            <Row label="Latitude"><input className="input" defaultValue={`+31°57'46"`} /></Row>
            <Row label="Longitude"><input className="input" defaultValue={`-034°47'19"`} /></Row>
            <Row label="Elevation (m)"><input className="input" defaultValue="20.0" /></Row>
            <div className="btn-row">
              <button className="btn">Sync From PC</button>
              <button className="btn">Upload</button>
              <button className="btn">Sites…</button>
            </div>
          </Section>

          <Section icon={IC.clock} title="Date / Time" showIcon={t.sectionIcons}>
            <Row label="Local">
              <input className="input" defaultValue="24/04/2026" style={{maxWidth:120}} />
              <input className="input" defaultValue="16:11:30" style={{maxWidth:100}} />
            </Row>
            <Row label="Timezone (h)">
              <input className="num" defaultValue="3.0" />
              <button className="btn" style={{marginLeft:6}}>Sync from PC</button>
            </Row>
            <button className="btn full">Upload</button>
            <Check checked={true}>Auto-sync date/time from PC on connect</Check>
          </Section>

          <Section icon={IC.track} title="Tracking / Slew" showIcon={t.sectionIcons}>
            <div style={{display:"flex", alignItems:"center", gap:16}}>
              <Check>Tracking enabled</Check>
              <div className={"status warn"+(t.statusPulse?"":" no-pulse")}><span className="dot"></span>State: Parked</div>
            </div>
            <Row label="Tracking rate"><select className="select flex"><option>Sidereal</option><option>Lunar</option><option>Solar</option></select></Row>
            <Row label="Guide rate (×)"><input className="num" defaultValue="0.50" /></Row>
            <div className="meridian-row">
              <div className="label">Slew</div>
              <input className="num sm" defaultValue="4.94" />
              <div className="hint">×</div>
              <div className="slider"><div className="track"><div className="fill" style={{width:"70%"}}></div></div><div className="knob" style={{left:"70%"}}></div></div>
              <div></div>
            </div>
            <Row label="At meridian">
              <select className="select" style={{width:140}}><option>Auto Flip</option><option>Pause</option><option>Continue</option></select>
              <button className="btn">Advanced…</button>
            </Row>
          </Section>

          <Section icon={IC.lim} title="Limits" showIcon={t.sectionIcons}>
            <div style={{display:"grid", gridTemplateColumns:"1fr 1fr", gap:"8px 16px"}}>
              <Row label="Horizon (°)"><input className="num" defaultValue="0" /></Row>
              <Row label="Overhead"><input className="num" defaultValue="90" /></Row>
              <Row label="Merid. E (min)"><input className="num" defaultValue="1" /></Row>
              <Row label="Merid. W"><input className="num" defaultValue="1" /></Row>
            </div>
          </Section>
        </div>

        {/* RIGHT PANE */}
        <div className="pane right scroll">
          <Section icon={IC.pos} title="Current Position" showIcon={t.sectionIcons}>
            <div className="readout">
              <div className="k">RA</div><div className="v">09:49:55.1899</div>
              <div className="k">Dec</div><div className="v">+65°57'20.891"</div>
              <div className="k">Altitude</div><div className="v">40.13<span className="unit">°</span></div>
              <div className="k">Azimuth</div><div className="v">28.13<span className="unit">°</span></div>
              <div className="k">Pier side</div><div className="v">W</div>
              <div className="k">LST / mount</div>
              <div className="v">05:40:59.0 <span className="unit">/</span> 05:40:59.5 <span className="delta">Δ 0.0 min</span></div>
            </div>
          </Section>

          <Section icon={IC.park} title="Park / Home / Go To" showIcon={t.sectionIcons}>
            <div className="grid-2">
              <button className="btn">Park</button>
              <button className="btn">Unpark</button>
              <button className="btn">Set Home</button>
              <button className="btn">Go Home</button>
            </div>
            <button className="btn full">Set Park Here</button>
            <button className="btn full primary">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="3"/><path d="M12 3v3M12 18v3M3 12h3M18 12h3"/></svg>
              Slew to Target…
            </button>
          </Section>

          <Section icon={IC.slew} title="Manual Slew" showIcon={t.sectionIcons}>
            <div className="slew-pad">
              {[
                ["NW","↖",""],["N","↑","active"],["NE","↗",""],
                ["W","←",""], ["STOP","","stop"],  ["E","→",""],
                ["SW","↙",""],["S","↓",""],        ["SE","↘",""],
              ].map(([label, arr, cls], i) => (
                <button key={i} className={"slew "+cls}>
                  {arr && <span className="dir">{arr}</span>}
                  {label}
                </button>
              ))}
            </div>
          </Section>

          <div style={{flex:1}}></div>
          <div className={"status info"+(t.statusPulse?"":" no-pulse")} style={{padding:"4px 2px"}}><span className="dot"></span>Connected clients: 0</div>
        </div>
      </div>

      {/* CONSOLE (collapsible section, same as others) */}
      <div className="console-block">
        <Section icon={IC.cons} title="Console" showIcon={t.sectionIcons}
          headerRight={
            <div style={{display:"flex", alignItems:"center", gap:12}}>
              <Check checked>Enable</Check>
              <Check checked>Auto-scroll</Check>
              <button className="btn sm">Copy</button>
              <button className="btn sm">Clear</button>
            </div>
          }>
          <div className="console-toolbar">
            <div className="manual">
              <input className="input" placeholder=":GVP#   — leading ':' and trailing '#' auto-added" />
              <button className="btn primary">Send</button>
            </div>
            <div className="filter">
              <input className="input" placeholder="Filter…" />
              <button className="btn sm">Clear filter</button>
            </div>
          </div>
          <div className="console" style={{height: t.consoleHeight}}>
            {LOG_LINES.map(([ts, cmd, resp, ms, blind], i) => (
              <div className="log-line" key={i}>
                <span className="ts">{ts}</span>
                <span className="cmd">{cmd}</span>
                <span className="arr">→</span>
                <span className={"resp"+(blind?" blind":"")}>{resp}</span>
                <span className="meta">{ms}</span>
              </div>
            ))}
          </div>
        </Section>
      </div>
    </div>
  );
}

function SitesPopup({ t }) {
  return (
    <div className="window popup-wrap" data-screen-label="02 Sites">
      <div className="titlebar">
        <div className="logo" style={{color:"var(--text-dim)"}}>{IC.pin}</div>
        <div className="title">Sites</div>
        <div className="spacer"></div>
        <button className="win-btn"><svg width="10" height="10" viewBox="0 0 10 10"><path d="M1 5h8" stroke="currentColor" strokeWidth="1.2"/></svg></button>
        <button className="win-btn"><svg width="10" height="10" viewBox="0 0 10 10"><rect x="1.5" y="1.5" width="7" height="7" fill="none" stroke="currentColor" strokeWidth="1.2"/></svg></button>
        <button className="win-btn close"><svg width="10" height="10" viewBox="0 0 10 10"><path d="M1 1l8 8M9 1l-8 8" stroke="currentColor" strokeWidth="1.2"/></svg></button>
      </div>

      <div className="sites-body">
        <div className="sites-list">
          <div className="sites-list-head">
            <div>Name</div><div>Latitude</div><div>Longitude</div><div>Elev (m)</div>
          </div>
          <div className="sites-list-body scroll">
            <div className="site-row selected"><div>Home</div><div>+31°57'46"</div><div>+034°47'20"</div><div>20.0</div></div>
            <div className="site-row"><div>Desert Camp</div><div>+30°37'12"</div><div>+034°48'02"</div><div>620.0</div></div>
            <div className="site-row"><div>Mount Observatory</div><div>+31°45'07"</div><div>+035°14'31"</div><div>865.0</div></div>
          </div>
        </div>

        <div className="sites-editor">
          <div className="f"><label>Name</label><input className="input" placeholder="e.g. Backyard" /></div>
          <div className="f"><label>Latitude (DMS or decimal)</label><input className="input" placeholder={`+31°57'46"`} /></div>
          <div className="f"><label>Longitude (west-positive, W is +)</label><input className="input" placeholder={`-034°47'19"`} /></div>
          <div className="f"><label>Elevation (m)</label><input className="input" placeholder="20.0" /></div>
          <button className="btn full">Load from current</button>
          <div className="btn-row">
            <button className="btn">Add</button>
            <button className="btn">Update</button>
            <button className="btn">Remove</button>
          </div>
          <div className="btn-row">
            <button className="btn">Import…</button>
            <button className="btn">Export…</button>
          </div>
        </div>
      </div>

      <div className="sites-footer">
        <button className="btn primary">Apply to mount</button>
        <button className="btn">Close</button>
      </div>
    </div>
  );
}

function App() {
  const [t, setTweak] = useTweaks(TWEAK_DEFAULTS);

  // Push accent into CSS vars
  useEffect(() => {
    const r = document.documentElement;
    r.dataset.theme = t.theme;
    r.style.setProperty("--accent", `oklch(${t.accentLightness} ${t.accentChroma} ${t.accentHue})`);
    r.style.setProperty("--accent-soft", `oklch(${t.accentLightness} ${t.accentChroma} ${t.accentHue} / 0.12)`);
    r.style.setProperty("--radius", t.radius + "px");
    r.style.setProperty("--font-sans", FONT_STACKS[t.fontFamily] || FONT_STACKS.Inter);
    r.style.setProperty("--font-mono", MONO_STACKS[t.monoFamily] || MONO_STACKS["JetBrains Mono"]);
    const d = DENSITY[t.density] || DENSITY.comfortable;
    r.style.setProperty("--gap", d.gap + "px");
    r.style.setProperty("--pad", d.pad + "px");
    r.style.setProperty("--row-h", d.row + "px");
  }, [t]);

  return (
    <>
      <div className="stack">
        <MainWindow t={t} setTweak={setTweak} />
        {t.showPopup && <SitesPopup t={t} />}
      </div>

      <TweaksPanel title="Tweaks">
        <TweakSection label="Theme" />
        <TweakRadio label="Mode" value={t.theme} options={["dark","light"]} onChange={(v)=>setTweak("theme", v)} />
        <TweakSlider label="Accent hue"        value={t.accentHue} min={0} max={360} step={1} unit="°"
                     onChange={(v)=>setTweak("accentHue", v)} />
        <TweakSlider label="Accent chroma"     value={t.accentChroma} min={0} max={0.30} step={0.01}
                     onChange={(v)=>setTweak("accentChroma", v)} />
        <TweakSlider label="Accent lightness"  value={t.accentLightness} min={0.35} max={0.85} step={0.01}
                     onChange={(v)=>setTweak("accentLightness", v)} />

        <TweakSection label="Typography" />
        <TweakSelect label="UI font"   value={t.fontFamily} options={Object.keys(FONT_STACKS)}
                     onChange={(v)=>setTweak("fontFamily", v)} />
        <TweakSelect label="Mono font" value={t.monoFamily} options={Object.keys(MONO_STACKS)}
                     onChange={(v)=>setTweak("monoFamily", v)} />

        <TweakSection label="Layout" />
        <TweakRadio  label="Density"  value={t.density} options={["compact","comfortable","spacious"]}
                     onChange={(v)=>setTweak("density", v)} />
        <TweakSlider label="Corner radius" value={t.radius} min={0} max={16} step={1} unit="px"
                     onChange={(v)=>setTweak("radius", v)} />
        <TweakSlider label="Console height" value={t.consoleHeight} min={140} max={360} step={10} unit="px"
                     onChange={(v)=>setTweak("consoleHeight", v)} />

        <TweakSection label="Details" />
        <TweakToggle label="Section icons"     value={t.sectionIcons} onChange={(v)=>setTweak("sectionIcons", v)} />
        <TweakToggle label="Pulse status dots" value={t.statusPulse}  onChange={(v)=>setTweak("statusPulse", v)} />
        <TweakToggle label="Show Sites popup"  value={t.showPopup}    onChange={(v)=>setTweak("showPopup", v)} />
      </TweaksPanel>
    </>
  );
}

ReactDOM.createRoot(document.getElementById("root")).render(<App/>);
