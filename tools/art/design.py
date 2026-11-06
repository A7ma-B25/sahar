"""Sahar design source: the generator used for the design canvas boards (shapes, moods). Do not edit by hand without updating the boards."""
import re
F="'IBM Plex Sans Arabic', system-ui, sans-serif"
MOODS={
 'calm':   dict(top='#FFFFFF', mid='#F1ECFF', bot='#CBC1F0', glow='#B9B0FF', bud='#C9B8FF', blush='#FFB8A0'),
 'happy':  dict(top='#FFFFFF', mid='#FFF0EA', bot='#FFC9B5', glow='#FFB08A', bud='#FFB08A', blush='#FF9F86'),
 'focus':  dict(top='#FFFFFF', mid='#EAF2FF', bot='#AFC9F2', glow='#6F9BEA', bud='#7FB0FF', blush='#C9B8F0'),
 'excited':dict(top='#FFFFFF', mid='#FFF6DE', bot='#FFD889', glow='#FFC24A', bud='#FFD24A', blush='#FFA27A'),
 'sleepy': dict(top='#E9E6F7', mid='#C9C3E6', bot='#8E88B8', glow='#5A5490', bud='#6C66A0', blush='#B6A6D6'),
 'hot':    dict(top='#FFFFFF', mid='#FFE6DA', bot='#FFAE8E', glow='#FF8A5C', bud='#FF9466', blush='#FF8F78'),
 'mystic': dict(top='#B7A8F0', mid='#7C69C9', bot='#3E2E86', glow='#7B5CFF', bud='#C6B5FF', blush='#9B7FE0'),
}
BODY ="M40 176 C 34 112, 72 58, 120 58 C 168 58, 206 112, 200 176 C 200 196, 184 206, 170 196 C 158 210, 136 212, 120 200 C 104 212, 82 210, 70 196 C 56 206, 40 196, 40 176 Z"
BODY2="M40 176 C 34 112, 72 58, 120 58 C 168 58, 206 112, 200 176 C 200 192, 186 210, 170 200 C 156 206, 136 214, 120 202 C 104 208, 84 214, 70 200 C 56 210, 40 192, 40 176 Z"
_n=[0]
def uid(p): _n[0]+=1; return f"{p}{_n[0]}"

def eyes(kind):
    if kind=='open': return ('<g><animateTransform attributeName="transform" type="scale" values="1 1;1 1;1 0.1;1 1" keyTimes="0;0.92;0.96;1" dur="6s" repeatCount="indefinite" additive="sum"/>'
        '<ellipse cx="100" cy="124" rx="9" ry="11" fill="#2A2350"/><ellipse cx="140" cy="124" rx="9" ry="11" fill="#2A2350"/>'
        '<circle cx="103" cy="119" r="3.4" fill="#FFFFFF"/><circle cx="143" cy="119" r="3.4" fill="#FFFFFF"/>'
        '<circle cx="97" cy="129" r="1.6" fill="#FFFFFF" opacity="0.8"/><circle cx="137" cy="129" r="1.6" fill="#FFFFFF" opacity="0.8"/></g>')
    if kind=='happy': return '<path d="M91 127 q9 -10 18 0 M131 127 q9 -10 18 0" fill="none" stroke="#2A2350" stroke-width="4.2" stroke-linecap="round"/>'
    if kind=='closed': return '<path d="M91 125 q9 6 18 0 M131 125 q9 6 18 0" fill="none" stroke="#2A2350" stroke-width="3.6" stroke-linecap="round"/>'
    if kind=='focus': return ('<ellipse cx="100" cy="126" rx="8" ry="8" fill="#2A2350"/><ellipse cx="140" cy="126" rx="8" ry="8" fill="#2A2350"/>'
        '<circle cx="103" cy="123" r="2.6" fill="#FFFFFF"/><circle cx="143" cy="123" r="2.6" fill="#FFFFFF"/>'
        '<path d="M90 111 l17 3 M150 111 l-17 3" stroke="#2A2350" stroke-width="2.6" stroke-linecap="round"/>')
    if kind=='sparkle': return ('<ellipse cx="100" cy="124" rx="10" ry="12" fill="#2A2350"/><ellipse cx="140" cy="124" rx="10" ry="12" fill="#2A2350"/>'
        '<path d="M103 113 l1.6 4 l4 1.6 l-4 1.6 l-1.6 4 l-1.6 -4 l-4 -1.6 l4 -1.6Z M143 113 l1.6 4 l4 1.6 l-4 1.6 l-1.6 4 l-1.6 -4 l-4 -1.6 l4 -1.6Z" fill="#FFFFFF"/>')
    if kind=='mystic': return ('<g><animate attributeName="opacity" values="1;1;0.2;1" keyTimes="0;0.85;0.9;1" dur="4s" repeatCount="indefinite"/>'
        '<ellipse cx="100" cy="124" rx="9" ry="11" fill="#1A1240"/><ellipse cx="140" cy="124" rx="9" ry="11" fill="#1A1240"/>'
        '<g><animateTransform attributeName="transform" type="translate" values="-3 0;3 0;3 0;-3 0;-3 0" keyTimes="0;0.2;0.5;0.7;1" dur="5s" repeatCount="indefinite"/>'
        '<circle cx="100" cy="124" r="4" fill="#E9DFFF"/><circle cx="140" cy="124" r="4" fill="#E9DFFF"/></g></g>')
def mouth(kind):
    return {'smile':'<path d="M114 141 q6 4.5 12 0" fill="none" stroke="#2A2350" stroke-width="2.6" stroke-linecap="round"/>',
     'grin':'<path d="M111 139 q9 10 18 0 Z" fill="#2A2350"/>',
     'flat':'<path d="M115 142 h10" stroke="#2A2350" stroke-width="2.6" stroke-linecap="round"/>',
     'o':'<ellipse cx="120" cy="142" rx="3.5" ry="4" fill="#2A2350"/>',
     'sleepy':'<path d="M116 143 q4 2 8 0" fill="none" stroke="#2A2350" stroke-width="2.4" stroke-linecap="round"/>',
     'mystic':'<path d="M114 141 q6 3 12 -1" fill="none" stroke="#1A1240" stroke-width="2.4" stroke-linecap="round"/>',
     'none':''}[kind]

def sahar(mood='calm', e='open', m='smile', moon=True, stars=True, floaty=True, headphones=False, extra_front='', extra_back='', leaf_anim=True, squish=False, size=(300,300), vb="0 0 300 300", aria=""):
    c=MOODS[mood]; g=uid('g'); gl=uid('gl'); lf=uid('lf'); mn=uid('mn')
    defs=(f'<defs><radialGradient id="{g}" cx="38%" cy="26%" r="90%"><stop offset="0" stop-color="{c["top"]}"/><stop offset="0.55" stop-color="{c["mid"]}"/><stop offset="1" stop-color="{c["bot"]}"/></radialGradient>'
          f'<radialGradient id="{gl}"><stop offset="0" stop-color="{c["glow"]}" stop-opacity="0.75"/><stop offset="1" stop-color="{c["glow"]}" stop-opacity="0"/></radialGradient>'
          f'<linearGradient id="{lf}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#C4ECC2"/><stop offset="1" stop-color="#6FB683"/></linearGradient>'
          f'<radialGradient id="{mn}" cx="35%" cy="30%" r="80%"><stop offset="0" stop-color="#FFF6D8"/><stop offset="1" stop-color="#F2C86A"/></radialGradient></defs>')
    orbit="M262 150 A 112 26 0 1 1 38 150 A 112 26 0 1 1 262 150"
    def moonsvg(front):
        vals = "1;0" if front else "0;1"
        return (f'<g transform="rotate(-10 150 150)"><g><animateMotion dur="9s" repeatCount="indefinite" path="{orbit}"/>'
                f'<animate attributeName="opacity" values="{vals}" keyTimes="0;0.5" calcMode="discrete" dur="9s" repeatCount="indefinite"/>'
                f'<circle r="20" fill="{c["glow"] if mood!="calm" else "#FFE7A8"}" opacity="0.35" filter="url(#s2Glow)"/>'
                f'<circle r="11" fill="url(#{mn})"/><circle cx="-3" cy="-2" r="2.2" fill="#E8B85A" opacity="0.6"/><circle cx="4" cy="3" r="1.5" fill="#E8B85A" opacity="0.6"/>'
                f'<path d="M-4 1 q1.5 1.5 3 0 M2 1 q1.5 1.5 3 0" fill="none" stroke="#8A6A2A" stroke-width="1" stroke-linecap="round"/></g></g>')
    back=''; front=''
    if moon:
        back+=f'<ellipse cx="150" cy="150" rx="112" ry="26" fill="none" stroke="{c["glow"]}" stroke-width="1.2" stroke-dasharray="2 6" opacity="0.55" transform="rotate(-10 150 150)"/>'
        back+=moonsvg(False); front+=moonsvg(True)
    if stars:
        back+=('<g><animateTransform attributeName="transform" type="rotate" values="0 150 140;360 150 140" dur="40s" repeatCount="indefinite"/>'
          +''.join(f'<use href="#s2Star" fill="#FFE7A8" transform="translate({x} {y}) scale({s})"><animate attributeName="opacity" values="0.3;1;0.3" dur="{d}s" repeatCount="indefinite"/></use>'
                   for x,y,s,d in [(150,22,0.9,2.6),(272,120,0.6,3.4),(46,70,0.7,2.2),(240,236,0.5,3),(36,214,0.55,2.8)])+'</g>')
    leaf=(f'<path d="M120 60 C 120 48, 121 38, 124 28" fill="none" stroke="#5E9E70" stroke-width="4" stroke-linecap="round"/>'
          f'<path d="M123 38 C 106 40, 94 30, 92 16 C 108 14, 120 24, 123 38 Z" fill="url(#{lf})"/><path d="M123 38 C 112 34, 102 26, 96 18" fill="none" stroke="#8CC795" stroke-width="1.2"/>'
          f'<path d="M124 32 C 138 30, 150 20, 152 8 C 136 8, 126 18, 124 32 Z" fill="url(#{lf})"/>'
          f'<circle cx="124" cy="27" r="9" fill="{c["bud"]}" opacity="0.5" filter="url(#s2Glow)"><animate attributeName="opacity" values="0.25;0.7;0.25" dur="2.4s" repeatCount="indefinite"/></circle>'
          f'<circle cx="124" cy="27" r="4" fill="{c["bud"]}"/><circle cx="123" cy="26" r="1.4" fill="#FFFFFF"/>')
    if leaf_anim: leaf=f'<g><animateTransform attributeName="transform" type="rotate" values="-5 120 60;5 120 60;-5 120 60" dur="5s" repeatCount="indefinite"/>{leaf}</g>'
    hp=''
    if headphones:
        hp=('<path d="M50 128 C 48 74, 192 74, 190 128" fill="none" stroke="#2F3363" stroke-width="7" stroke-linecap="round"/>'
            '<rect x="36" y="114" width="20" height="34" rx="9" fill="#2F3363"/><rect x="184" y="114" width="20" height="34" rx="9" fill="#2F3363"/>'
            f'<rect x="40" y="120" width="6" height="22" rx="3" fill="{c["bud"]}"/><rect x="194" y="120" width="6" height="22" rx="3" fill="{c["bud"]}"/>')
    body=(f'<path d="{BODY}" fill="url(#{g})">'+(f'<animate attributeName="d" values="{BODY};{BODY2};{BODY}" dur="2.2s" repeatCount="indefinite"/>' if floaty else '')+'</path>'
          f'<g fill="none" stroke="{c["bot"]}" stroke-width="1.8" stroke-linecap="round" opacity="0.4"><path d="M120 62 q-9 3 -16 10"/><path d="M120 62 q9 3 16 10"/></g>'
          f'<path d="M58 104 C 70 78, 92 66, 112 64" fill="none" stroke="#FFFFFF" stroke-width="7" stroke-linecap="round" opacity="0.75"/>'
          f'<ellipse cx="78" cy="142" rx="12" ry="6.5" fill="{c["blush"]}" opacity="0.55" filter="url(#s2Blur)"/><ellipse cx="162" cy="142" rx="12" ry="6.5" fill="{c["blush"]}" opacity="0.55" filter="url(#s2Blur)"/>')
    char=f'<g transform="translate(30 30)">{leaf}{body}{eyes(e)}{mouth(m)}{hp}</g>'
    if squish:
        char=('<g transform="translate(150 236)"><g><animateTransform attributeName="transform" type="scale" values="1 1;1 1;1.14 0.84;0.94 1.08;1.03 0.97;1 1" keyTimes="0;0.3;0.42;0.55;0.68;1" dur="2.4s" repeatCount="indefinite"/>'
              f'<g transform="translate(-150 -236)">{char}</g></g></g>')
    if floaty:
        char=f'<g><animateTransform attributeName="transform" type="translate" values="0 0;0 -10;0 0" dur="3.6s" repeatCount="indefinite"/>{char}</g>'
        shadow=('<ellipse cx="150" cy="268" rx="62" ry="8" fill="#1E2350" opacity="0.16"><animate attributeName="rx" values="62;50;62" dur="3.6s" repeatCount="indefinite"/>'
                '<animate attributeName="opacity" values="0.16;0.08;0.16" dur="3.6s" repeatCount="indefinite"/></ellipse>')
    else: shadow=''
    halo=f'<circle cx="150" cy="150" r="120" fill="url(#{gl})" opacity="0.6"><animate attributeName="opacity" values="0.45;0.7;0.45" dur="4s" repeatCount="indefinite"/></circle>'
    return (f'<svg width="{size[0]}" height="{size[1]}" viewBox="{vb}" style="overflow: visible" role="img" aria-label="{aria}">{defs}'
            f'{halo}{shadow}{extra_back}{back}{char}{front}{extra_front}</svg>')

SHARED='''<svg width="0" height="0" style="position: absolute" aria-hidden="true"><defs>
<filter id="s2Blur" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="3"/></filter>
<filter id="s2Glow" x="-100%" y="-100%" width="300%" height="300%"><feGaussianBlur stdDeviation="6"/></filter>
<path id="s2Star" d="M0 -8 L2 -2 L8 0 L2 2 L0 8 L-2 2 L-8 0 L-2 -2Z"/>
</defs></svg>'''

def tile(bg, title, sub, svg, dark=False, h=290):
    c1="#FFFFFF" if dark else "#1E2350"; c2="#C9CDEB" if dark else "#4A4E78"
    return (f'<figure style="margin: 0; height: {h}px; border-radius: 24px; background: {bg}; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 6px; overflow: hidden">'
            f'{svg}<figcaption style="font-size: 17px; font-weight: 600; color: {c1}">{title}</figcaption><span style="font-size: 13px; color: {c2}; text-align: center; padding: 0 12px">{sub}</span></figure>')

MOODS['alert']=dict(top='#FFFFFF', mid='#FFF0E6', bot='#FFC7A6', glow='#F2A06E', bud='#F2A06E', blush='#FF9F86')
MOODS['dim']=dict(top='#F1EEF8', mid='#DCD6EE', bot='#ABA3CC', glow='#E5484D', bud='#B0A8CC', blush='#C9B8E0')
MOODS['error']=dict(top='#FFFFFF', mid='#FFEDEE', bot='#F4B5B8', glow='#E5484D', bud='#E5484D', blush='#FF9F9F')
MOODS['online']=dict(top='#FFFFFF', mid='#E9FBF8', bot='#A9E4DC', glow='#5CC9BC', bud='#5CC9BC', blush='#FFB0A0')
MOODS['cold']=dict(top='#FFFFFF', mid='#EEF7FF', bot='#BFDDF7', glow='#8CC8F2', bud='#8CC8F2', blush='#C9D8F0')
MOODS['love']=dict(top='#FFFFFF', mid='#FFEFF4', bot='#FFC2D3', glow='#FF8FA3', bud='#FF8FA3', blush='#FF8FA3')
MOODS['night']=dict(top='#E9E4FF', mid='#B6A9EC', bot='#6E5CC0', glow='#9C86FF', bud='#FFE7A8', blush='#C9B8F0')
_E=eyes
def eyes(kind):
    if kind=='worried': return ('<ellipse cx="100" cy="126" rx="8.5" ry="10" fill="#2A2350"/><ellipse cx="140" cy="126" rx="8.5" ry="10" fill="#2A2350"/>'
        '<circle cx="103" cy="122" r="3" fill="#FFFFFF"/><circle cx="143" cy="122" r="3" fill="#FFFFFF"/>'
        '<path d="M90 110 l16 -5 M150 110 l-16 -5" stroke="#2A2350" stroke-width="2.6" stroke-linecap="round"/>')
    if kind=='wide': return ('<ellipse cx="100" cy="124" rx="10.5" ry="13" fill="#2A2350"/><ellipse cx="140" cy="124" rx="10.5" ry="13" fill="#2A2350"/>'
        '<circle cx="104" cy="118" r="4" fill="#FFFFFF"/><circle cx="144" cy="118" r="4" fill="#FFFFFF"/>')
    if kind=='look': return ('<g><animateTransform attributeName="transform" type="translate" values="-5 0;5 0;-5 0" dur="4s" repeatCount="indefinite"/>'
        '<ellipse cx="100" cy="124" rx="9" ry="11" fill="#2A2350"/><ellipse cx="140" cy="124" rx="9" ry="11" fill="#2A2350"/>'
        '<circle cx="103" cy="119" r="3.4" fill="#FFFFFF"/><circle cx="143" cy="119" r="3.4" fill="#FFFFFF"/></g>')
    if kind=='up': return ('<ellipse cx="100" cy="124" rx="9" ry="11" fill="#2A2350"/><ellipse cx="140" cy="124" rx="9" ry="11" fill="#2A2350"/>'
        '<circle cx="104" cy="116" r="3.6" fill="#FFFFFF"/><circle cx="144" cy="116" r="3.6" fill="#FFFFFF"/>')
    if kind=='wink': return ('<ellipse cx="100" cy="124" rx="9" ry="11" fill="#2A2350"/><circle cx="103" cy="119" r="3.4" fill="#FFFFFF"/>'
        '<path d="M131 127 q9 -10 18 0" fill="none" stroke="#2A2350" stroke-width="4.2" stroke-linecap="round"/>')
    if kind=='half': return ('<path d="M91 122 h18 M131 122 h18" stroke="#2A2350" stroke-width="3" stroke-linecap="round"/>'
        '<path d="M92 123 a8 6 0 0 0 16 0 Z M132 123 a8 6 0 0 0 16 0 Z" fill="#2A2350"/>')
    if kind=='dizzy': return ('<g fill="none" stroke="#2A2350" stroke-width="2.6" stroke-linecap="round">'
        '<path d="M100 124 m-2 0 a2 2 0 1 1 4 0 a5 5 0 1 1 -10 0 a8 8 0 1 1 16 0"><animateTransform attributeName="transform" type="rotate" values="0 100 124;360 100 124" dur="1.2s" repeatCount="indefinite"/></path>'
        '<path d="M140 124 m-2 0 a2 2 0 1 1 4 0 a5 5 0 1 1 -10 0 a8 8 0 1 1 16 0"><animateTransform attributeName="transform" type="rotate" values="0 140 124;360 140 124" dur="1.2s" repeatCount="indefinite"/></path></g>')
    if kind=='heart': return ''.join(f'<path d="M{x} 132 c-7 -6 -14 -12 -9 -18 c3 -4 8 -3 9 1 c1 -4 6 -5 9 -1 c5 6 -2 12 -9 18 Z" fill="#E5486D"/>' for x in (100,140))
    if kind=='sad': return ('<ellipse cx="100" cy="127" rx="8.5" ry="9.5" fill="#2A2350"/><ellipse cx="140" cy="127" rx="8.5" ry="9.5" fill="#2A2350"/>'
        '<circle cx="102" cy="123" r="3" fill="#FFFFFF"/><circle cx="142" cy="123" r="3" fill="#FFFFFF"/>'
        '<path d="M89 114 l17 -6 M151 114 l-17 -6" stroke="#2A2350" stroke-width="2.4" stroke-linecap="round"/>'
        '<path d="M92 138 q-3 6 0 9 q3 -3 0 -9" fill="#8CC8F2"/>')
    return _E(kind)
_M=mouth
def mouth(kind):
    extra={'wobble':'<path d="M112 143 q4 -3 8 0 q4 3 8 0" fill="none" stroke="#2A2350" stroke-width="2.4" stroke-linecap="round"/>',
     'bigo':'<ellipse cx="120" cy="144" rx="5" ry="6.5" fill="#2A2350"/>',
     'frown':'<path d="M114 145 q6 -5 12 0" fill="none" stroke="#2A2350" stroke-width="2.6" stroke-linecap="round"/>',
     'tongue':'<path d="M111 139 q9 9 18 0 Z" fill="#2A2350"/><path d="M116 143 q4 6 8 0 Z" fill="#FF8FA3"/>',
     'puff':'<path d="M113 142 h14" stroke="#2A2350" stroke-width="2.6" stroke-linecap="round"/><circle cx="108" cy="141" r="3" fill="none" stroke="#2A2350" stroke-width="1.6"/>',
     'sing':'<ellipse cx="120" cy="143" rx="5" ry="6" fill="#2A2350"/>',
     'yawn':'<ellipse cx="120" cy="145" rx="7" ry="9" fill="#2A2350"/>'}
    return extra.get(kind) or _M(kind)

def mini(mood='calm', e='open', m='smile', s=70, **kw):
    kw.setdefault('stars', False)
    svg=sahar(mood,e,m,size=(s,s),vb="0 0 300 300",**kw)
    return re.sub(r'^<svg[^>]*>', f'<svg x="{-s/2}" y="{-s/2}" width="{s}" height="{s}" viewBox="0 0 300 300" overflow="visible">', svg, count=1)

def eorb(emoji, c1, c2, glow, cx=258, cy=92, r=24, anim=''):
    gid=uid('ob')
    return (f'<defs><radialGradient id="{gid}" cx="35%" cy="30%" r="80%"><stop offset="0" stop-color="{c1}"/><stop offset="1" stop-color="{c2}"/></radialGradient></defs>'
            f'<g transform="translate({cx} {cy})"><g><animateTransform attributeName="transform" type="translate" values="0 0;0 -6;0 0" dur="3.2s" repeatCount="indefinite"/>{anim}'
            f'<circle r="{r+12}" fill="{glow}" opacity="0.45" filter="url(#s2Glow)"/><circle r="{r}" fill="url(#{gid})"/>'
            f'<circle r="{r}" fill="none" stroke="#FFFFFF" stroke-width="1.5" opacity="0.6"/>'
            f'<text y="8" font-size="22" text-anchor="middle" font-family="Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji, sans-serif">{emoji}</text></g></g>')
def floattxt(x,y,t,c,sz=18,b=0):
    return (f'<text x="{x}" y="{y}" font-size="{sz}" font-weight="600" fill="{c}" text-anchor="middle" font-family="{F}">{t}'
            f'<animateTransform attributeName="transform" type="translate" values="0 6;0 -10" dur="2s" begin="{b}s" repeatCount="indefinite"/><animate attributeName="opacity" values="0;1;0" dur="2s" begin="{b}s" repeatCount="indefinite"/></text>')
def floatemoji(x,y,em,sz=20,b=0,dur=2.4):
    return (f'<text x="{x}" y="{y}" font-size="{sz}" text-anchor="middle" font-family="Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji, sans-serif">{em}'
            f'<animateTransform attributeName="transform" type="translate" values="0 8;0 -14" dur="{dur}s" begin="{b}s" repeatCount="indefinite"/><animate attributeName="opacity" values="0;1;0" dur="{dur}s" begin="{b}s" repeatCount="indefinite"/></text>')
def particles(kind):
    if kind=='snow': return ''.join(f'<circle cx="{x}" cy="0" r="3" fill="#FFFFFF" stroke="#BFDDF7"><animateTransform attributeName="transform" type="translate" values="0 0;6 260" dur="{d}s" begin="{b}s" repeatCount="indefinite"/></circle>' for x,d,b in [(40,4,0),(90,5,1),(200,4.5,0.5),(250,3.8,1.5),(150,5,2)])
    if kind=='rain': return ''.join(f'<path d="M{x} 0 l-4 12" stroke="#7FA8D9" stroke-width="3" stroke-linecap="round"><animateTransform attributeName="transform" type="translate" values="0 0;-10 260" dur="1.2s" begin="{b}s" repeatCount="indefinite"/></path>' for x,b in [(40,0),(100,0.4),(170,0.2),(230,0.7),(280,0.5)])
    if kind=='wind': return ''.join(f'<path d="M0 {y} q40 -10 80 0 t80 0" fill="none" stroke="#B9C9E6" stroke-width="3" stroke-linecap="round" opacity="0.7"><animateTransform attributeName="transform" type="translate" values="-160 0;300 0" dur="{d}s" begin="{b}s" repeatCount="indefinite"/></path>' for y,d,b in [(80,2,0),(170,2.4,0.8),(230,2.2,0.4)])
    if kind=='confetti': return ''.join(f'<rect x="{x}" y="0" width="6" height="10" rx="2" fill="{c}"><animateTransform attributeName="transform" type="translate" values="0 0;0 260" dur="{d}s" begin="{b}s" repeatCount="indefinite"/></rect>' for x,c,d,b in [(40,'#FF8FA3',3,0),(90,'#FFC24A',3.4,0.6),(160,'#7FD6CF',2.8,0.3),(220,'#B9B0FF',3.2,1),(270,'#9EE493',3,0.5)])
    if kind=='fireworks': return ''.join(f'<g transform="translate({x} {y})"><g stroke="{c}" stroke-width="3" stroke-linecap="round"><animate attributeName="opacity" values="0;1;0" dur="1.4s" begin="{b}s" repeatCount="indefinite"/><path d="M0 -8 v-10 M8 0 h10 M0 8 v10 M-8 0 h-10 M6 -6 l7 -7 M6 6 l7 7 M-6 6 l-7 7 M-6 -6 l-7 -7"/></g></g>' for x,y,c,b in [(50,50,'#FFC24A',0),(250,40,'#FF8FA3',0.5),(40,180,'#7FD6CF',0.9)])
    if kind=='hearts': return floatemoji(50,90,'💗',18,0)+floatemoji(70,60,'💕',14,0.8)
    if kind=='zzz': return floattxt(220,60,'z','#B4B9DA',24,0)+floattxt(240,40,'z','#B4B9DA',16,1)
    if kind=='sweat': return '<path d="M200 96 q-6 10 0 14 q6 -4 0 -14" fill="#8CC8F2"><animateTransform attributeName="transform" type="translate" values="0 0;0 14" dur="1.4s" repeatCount="indefinite"/><animate attributeName="opacity" values="1;0" dur="1.4s" repeatCount="indefinite"/></path>'
    if kind=='notes': return floatemoji(60,80,'🎵',18,0)+floatemoji(240,70,'🎶',18,1)
    if kind=='stars': return ''.join(f'<use href="#s2Star" fill="#FFE7A8" transform="translate({x} {y}) scale({k})"><animate attributeName="opacity" values="0.2;1;0.2" dur="{d}s" repeatCount="indefinite"/></use>' for x,y,k,d in [(40,40,0.9,2),(260,30,0.7,2.6),(270,200,0.6,3),(30,190,0.7,2.4)])
    return ''

def tile(bg, title, sub, svg, dark=False, h=262, num=None):
    c1="#FFFFFF" if dark else "#1E2350"; c2="#C9CDEB" if dark else "#4A4E78"
    badge=f'<span style="position: absolute; top: 10px; inset-inline-start: 12px; font-size: 11px; font-weight: 600; color: {c2}; opacity: 0.8">{num}</span>' if num else ''
    return (f'<figure style="position: relative; margin: 0; height: {h}px; border-radius: 22px; background: {bg}; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 4px; overflow: hidden">{badge}'
            f'{svg}<figcaption style="font-size: 15px; font-weight: 600; color: {c1}; text-align: center; padding: 0 8px">{title}</figcaption><span style="font-size: 12px; color: {c2}; text-align: center; padding: 0 12px; line-height: 1.5">{sub}</span></figure>')

