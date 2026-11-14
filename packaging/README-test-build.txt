سَحَر · Sahar — نسخة تجريبية 0.12.0 (ويندوز)
=============================================

شو الجديد بـ 0.14 — «عقلها»: بتفكّر من جوّا جهازك 🌙
-------------------------------------------------
- عقل محلّي: سحر بتشغّل نموذج لغة على جهازك بلا نت (llama.cpp، نسخة b12207 الرسمية، Vulkan، جوّا هالـ zip بمجلد runtime).
  بيشتغل بس وقت الحاجة: أول ما تكتبلها بالقائمة بيصحى، وبعد ٥ دقايق بلا حكي (أو دقيقة عالبطارية) بيطلع من الذاكرة كلياً — ٠ ميغا وهو نايم.
  إذا سكّرت سحر، العقل بيروح معها.
- التنزيل من جوّاها، بإذنك بس: من «النماذج» («نزّليه») أو من فقاعتها لما تسألك («إي، نزّليه» / «بعدين» / «لا»). بتقلك الحجم أول
  (Gemma 4 E4B ≈ ٤٫٩٨ غيغا). بتجرّب أول إذا Hugging Face بيفتح عندك؛ إذا مسكّر بتقلك بوضوح واقترح تشغّل الـ VPN.
  بيوقف وبيكمّل حتى بعد ما تطفي الجهاز، وبتتأكّد من الملف (SHA-256) قبل ما تستعمله. الفقاعة بتخبّرك وين صار (ربع، نص، تلت تربع).
- أي نموذج بدك: المقترحة (Gemma 4 E4B الأساسي، Jais 3 المنافس، Gemma 4 E2B الخفيف)، أو «زيدي نموذج» برابط أو اسم من
  Hugging Face (بتثبّت النسخة والـ SHA-256 وقتها)، أو ملف ‎.gguf‎ عندك. كل نموذج إله «جرّبيه»: السرعة، كرت الشاشة، الحكي الشامي
  (٣٠ سؤال)، والأوامر (٣٠ أمر). إذا ما بيعرف ينفّذ أوامر بيطلعلو «حكي بس» والأوامر («مؤقت ٥») بتضل متل قبل.
  تغيير النموذج ما بيمسح ذاكرتها ولا شو فهمت عنك.
- جهازك: بتعرف لحالها شو بيلبقلو (٣٢ غيغا + RTX 4050 لابتوب ← Gemma 4 E4B كلو عكرت الشاشة). «استعملي كرت الشاشة» و«طريقة
  التشغيل» (تلقائي / Vulkan / CUDA — CUDA تنزيل زيادة ≈ ٦٨٠ ميغا بس إذا قلت إي). إذا الكرت ما زبط بترجع للمعالج لحالها.
- صفحات جديدة بالإعدادات: «عقلي» و«النماذج» (والصفحتين «عنك» و«شو عملت اليوم»). «عن سحر» بتذكر llama.cpp (MIT) والنماذج (Apache 2.0).
- السجل (engine.log و mind.log) بيكتب أنواع وأرقام وأوقات بس — أبداً شو كتبت ولا شو جاوبت ولا اسم ملف.

جرّب بإيدك: افتح «النماذج» وكبّس «نزّليه» (≈ ٥ غيغا)؛ وقّفه وكمّله؛ سكّر سحر وافتحها وكمّل؛ لما يخلص كبّس «جرّبيه» وصوّرلنا
الأرقام؛ جرّب كمان مع الـ VPN مطفي إذا Hugging Face مسكّر عندك (لازم تقلك بوضوح).

شو الجديد بـ 0.13 — «النشاطات»: بتحمل معها شو عم يصير 🏷️
-------------------------------------------------
- تعليقة صغيرة بتتعلّق بخيط تحتها، فيها شي واحد بسطر: الأغنية (مع غلافها ومشوارها)، الطقس، Claude Code، الرف، النسخ،
  الصوت، البطارية، الكاميرا والمايك، «وإنت غايب»، ووقت الشاشة. الباقي حبّات عالخيط (لحد ٣ وبعدين عقدة).
  عجلة الماوس فوق التعليقة بتقلّب بينهن، وكبسة عالحبّة بتجيبها قدّام. كبسة عالتعليقة بتفتح كرتها جنبها (Esc بيسكّر).
  إذا ما في مطرح تحتها (جنب شريط المهام) بتتعلّق عالجنب. وقت القائمة أو الكرت مفتوح، أو وهي مخبّاية أو عم تنسحب، بتختفي.
- الترتيب: Claude ناطر جوابك، بعدين اللحظات القصيرة (٦ ثواني؛ الصوت ثانية ونص)، النسخ والمؤقت، Claude عم يشتغل، الموسيقى،
  الكاميرا والمايك، الرف، الموسيقى الواقفة (١٠ دقايق)، والطقس بس إذا ما في شي تاني.
- الموسيقى: من أي برنامج (أحداث الويندوز، بلا فحص كل ثانيتين). الكرت فيه الغلاف والوقت ويلي قبل / شغّل-وقّف / يلي بعد.
  «الكلمات» مطفية من الأول: أول مرة بتسألك قبل ما يطلع شي عالإنترنت (lrclib.net، بس اسم الأغنية والفنان وطولها).
  وهي عم تسمع: السماعات، وهالتها بلون الغلاف (فاتح)، وبتتمايل شوي عالإيقاع.
- الطقس: اليوم عالتعليقة وكرت فيه الساعات الخمس الجاية، وتحذير مرة وحدة لكل موجة: مطر، عاصفة، ريح، حر، صقيع، هوا مو نضيف.
  إذا ما اخترت مدينة للطقس بياخد مدينة الصلاة (قرارك). مع المطر ورقتها بتنزل شوي متل الشمسية.
- Claude Code: «اربطها» من الإعدادات بتزيد hooks على إعداداته (بتحفظ نسخة من القديم). بتشوف شو عم يعمل ومن قديش،
  ولما يسألك إذن بتطلع «Claude ناطر جوابك» أول شي مع وجهها المنتبه — بس الجواب دايماً من نافذة Claude نفسها (قرارك: بلا أزرار).
- الرف: افلت ملفات عليها بتمسكهن (بتنعصر شوي). اللقطات الجديدة بتنزل لحالها. من الكرت: اسحب وحدة لبرا (نسخة بس، الأصل بيضل)،
  انسخ النص يلي بالصورة، صغّرها، اضغطهن، أو شارك (بيفتح المستكشف عالملف).
- الجهاز، النسخ (نافذة النسخ تبع الويندوز، النسبة والوقت الباقي)، «وإنت غايب» (لما ترجع بعد دقيقتين وأكتر بتقلك شو صار
  وبتنط من الفرح)، ووقت الشاشة (مطفي من الأول، بيضل على جهازك ٧ أيام).
- ملء الشاشة على شاشتها: بس الأشيا المهمة (Claude بدّه إذنك، عاصفة/ريح/حر، بطارية ضعيفة) بتدق من فوق الشاشة ٤ ثواني وبترجع،
  بلا أزرار. يلي بيخلص وقتها بينحفظ لـ«وإنت غايب».
- صفحة جديدة بالإعدادات: «النشاطات» — كل نشاط بينطفى لحالو، الكلمات، قديش تضل الموسيقى الواقفة، مدينة الطقس، ربط Claude Code،
  واللقطات الجديدة عالرف. الخصوصية صارت تسمّي كل موقع بيحكي معه: open-meteo.com و lrclib.net.
- السجل بيكتب أنواع وأعداد بس — أبداً اسم أغنية ولا ملف ولا أمر ولا مدينة.

جرّب بإيدك: شغّل أغنية؛ افلت ملف عليها؛ خود لقطة شاشة؛ انسخ مجلد كبير؛ قفّل الجهاز دقيقتين وارجع؛ اربط Claude Code وخلّيه يشتغل
وبعدين يسألك إذن؛ شغّل فيديو ملء شاشة وخلّي Claude يسألك (لازم تدق من فوق).

شو الجديد بـ 0.12.0 — «إعداداتي»: برنامج إعدادات إلها ⚙️
-------------------------------------------------
- زر «إعداداتي» بقائمتها (وبقائمة أيقونتها جنب الساعة) بيفتح نافذة إعدادات هادية بنفس ألوانها، فاتحة أو غامقة متل الويندوز.
  كل تغيير بيتطبّق فوراً، بلا زر حفظ. النافذة وحدة بس: إذا فتحتها مرة تانية بتجيك يلي مفتوحة. ولما تسكّرها بتطلع كلياً.
  ما بتفتح الإنترنت أبداً: البحث عن مدينتك بيمرق من سَحَر نفسها.
- الصفحات: سحورة · القائمة / الصلاة · الاستماع / عام · الخصوصية / عن سحر. (الأصوات و Claude Code مخبّايين لحد ما يشتغلوا.)
- سحورة: سَحَر صغيرة حيّة بالصفحة بتنط وبتحكيلك كلمة مع كل تغيير. اسمك، شب ولا صبية (كلامها بيتغيّر معك: «اكتبي اسمك»)،
  حجمها (صغيرة / وسط / كبيرة — وهي عالشاشة بتكبر وبتصغر فوراً)، نشاطها، وشو بتعمل لحالها: تعلّق على شغلك، مزاجها يتغيّر لحالو،
  تلعب لحالها، تحس بالطقس — والنجوم جواتها.
- القائمة: شو بيطلع بقائمتها (كل شي بينطفى ويشتغل)، شو بيبيّن فوق، بشو بتنفتح ووين، واختصار الكيبورد
  (اكبس «غيّر» واكبس الاختصار الجديد؛ إذا مأخود أو بتستعمله البرامج بتقلّك ليش وبتضل عالقديم).
- الصلاة: أوقات اليوم الخمسة والجاية مع عدّاد، المدينة أو موقع الويندوز، طريقة الحساب، العصر (الجمهور / حنفي)، والتذكير وقديش قبل.
- الاستماع: مين القارئ. عام: فاتح / غامق / متل الويندوز، واللغة (عربي أو English — هلق للإعدادات بس).
- الخصوصية: امسح يلي نسخته، الطقس، و«امسح كل شي عني» (بتنسى كل شي وبترجع تتعرّف عليك من جديد).
- إذا ملف إعداداتها خرب وما عم تقدر تقراه: بتطلع ملاحظة هادية فوق كل صفحة مع «نبلّش من جديد».
  بتسألك «أكيد؟»، وبعدين بترجع لإعداداتها الأولى وبتسألك عن اسمك من جديد. الملف الخربان بيضل على جنب (.bad) وملاحظاتك متل ما هي.
- حجمها من الإعدادات صار يتطبّق فعلاً (قبل كان دايماً وسط).
- الملف المضغوط صار أكبر بحوالي 30 ميغا: برنامج الإعدادات جوّا مجلد Sahar.Settings جنب Sahar.Engine.exe — خلّيهن سوا.
  بيحتاج "WebView2" من مايكروسوفت (موجود بويندوز 11 وبأغلب ويندوز 10). إذا مش موجود، زر «إعداداتي» ما بيطلع.

شو جرّب: افتح «إعداداتي» وغيّر حجمها وشوفها عالشاشة؛ طفّي «نلعب؟» من صفحة القائمة وافتح قائمتها؛ غيّر الاختصار لـ Ctrl+Alt+K
وجرّبه؛ خلّي الويندوز غامق وشوف النافذة؛ سكّر النافذة وتأكد إنها راحت من مدير المهام.

من 0.11.0 — قائمتها الجديدة وصندوق الحكي 💬
الجديد بالنسخة الجاية — مثبّت، وتحديثات بتسألك عنها 📦
-------------------------------------------------
- صار في مثبّت: Sahar-Setup-0.11.0.exe. بتشغّلو مرة وحدة وخلص، بلا فك ضغط.
  بيتثبّت إلك انت بس (بلا صلاحيات أدمن) بمجلد %LocalAppData%\Programs\Sahar،
  بيحط «سحر» بقائمة ابدأ، وبيسألك إذا بدك ياها تشتغل مع الويندوز (معلّمة من حالها).
- لما تطلع نسخة جديدة، سَحَر بتقلك بفقاعتها: «في نسخة جديدة مني …، بدك ثبّتها؟» وتحتها «إي» و«بعدين».
  «إي»: بتنزّلها، بتتأكد إنها منّا نحنا (توقيع رقمي) وإنها نفس الملف بالضبط، والمثبّت بيسكّرها وبيثبّت وبيرجّعها بعد ثواني.
  إذا ما زبط التثبيت لأي سبب، بترجع متل ما كانت وبتقلك «ما زبط التحديث».
  إذا كنت مشغّلها من الـ zip: بيطلعلك المثبّت لتشوف وين رح تنحط وتختار إذا بدها تشتغل مع الويندوز، وبعدها بتقلك وين صارت ساكنة.
  «بعدين» (أو إذا سكّرت الفقاعة): بتسألك مرة تانية بكرا. أبداً ما بتحدّث حالها بلا ما تقلها «إي».
- بتدوّر دقيقتين بعد ما تشتغل، وبعدين كل 12 ساعة. وبإيدك: كبسة يمين على أيقونتها ← «دوّر على تحديث».
- إعداداتك وملاحظاتك وسجلّها بيضلّوا متل ما هنن وقت التحديث أو لما تثبّتها فوق حالها.
- الإزالة: إعدادات الويندوز ← التطبيقات ← سَحَر ← إزالة. بتسألك إذا بدك تمسح إعداداتها كمان (الجواب العادي «لا»).
- صار إلها أيقونة: وجّها 🌱 (بقائمة ابدأ، جنب الساعة، وبالإعدادات ← التطبيقات). شوف docs/design/icon/icon-sheet.png
- إذا شغّلت مثبّت أقدم من النسخة يلي عندك، بيسألك قبل ما يبدّلها.
- النت: بتحكي بس مع github.com (ملفات النسخة)، وما بتبعت شي عنك غير اسمها ورقم نسختها.
  إذا ما في نت بتسكت وبتجرّب بعدين (والسجل بيكتب سطر واحد).

جرّب بإيدك: ثبّتها من Sahar-Setup-0.11.0.exe؛ دوّر على «سحر» بقائمة ابدأ؛ أعد تشغيل الجهاز وشوف إذا طلعت لحالها؛
شغّل المثبّت مرة تانية وهي شغّالة (لازم تسكّر وترجع، وإعداداتك متل ما هي)؛ «دوّر على تحديث» من أيقونتها؛
وأزلها من الإعدادات وجاوب «لا» (لازم ترجع متل ما كانت إذا رجعت ثبّتها).
شكل المثبّت بالعربي (من سيرفر الفحص): docs/design/installer/wizard-ar.png

شو الجديد بـ 0.11.0 — قائمتها الجديدة وصندوق الحكي 💬
-------------------------------------------------
- القائمة الدائرية راحت. كبسة يمين عليها (أو Ctrl+Alt+S من أي مكان) بتفتح بطاقة هادية جنبها:
  فوق سطرها هي «شو بدك نعمل؟» وتحتو أهم شي هلق (المؤقت، الصلاة الجاية، القرآن، أو قديش باقي لترجع تحكي)،
  وتحتو صندوق حكي، وبلاطاتها التلاتة (نلعب؟ · بسكت شوي · بغفى شوي)، وأربع أدوات (مؤقت · ملاحظة · استماع · الحافظة).
  قمرها بيقعد على زاوية البطاقة يلي جنبها لما تكون مفتوحة.
- الكبستين عليها ما عادوا يفتحوا القائمة: صاروا نكشتين (نطّة وضحكة) متل ما طلبت.
- بالكيبورد: Tab والأسهم بيتنقّلوا، ١–٣ بيختاروا البلاطات، Enter بيفتح، Esc بيرجع وبعدين بيسكّر، والتركيز بيرجع للبرنامج يلي كنت فيه.
- صندوق الحكي (محلي، بلا ذكاء اصطناعي لسا) — جرّب تكتب:
  «مؤقت ٥» أو "timer 5" · «ملاحظة اشتري خبز» · «اسكتي» أو «اسكتي ساعة» · «نامي» · «نلعب» أو «نلعب الحقني / غمّضلي / الطابة / النجوم»
  · «ارقصي» · «نطّي» · «تمطّي» · «الصلاة» · «كيفك» — وأي شي تاني بتقلّك إنها لسا عم تتعلّم.
  الأرقام بأي شكل (٥ أو 5). بترد بسطرها فوق وبتنط. السجل بيكتب نوع الأمر بس (مثلاً chat: Timer)، أبداً مو يلي كتبتو.
- وجهها صار يتغيّر متل الرمشة: بتسكّر عيونها، بيتبدّل الوجه وهي مسكّرة، وبتفتح — ما عاد يبيّن وجهين فوق بعض.
  والنكشة صارت شغلة وحدة: نطّة مع وجه «^ ^» (بلا الوجه الزهري يلي كان يطلع قبل).
- لما تفتح القائمة أو تبلّش لعبة ما عاد ترسم بمكان غلط ولا للحظة (نافذتها بتنقل لنافذة احتياط جاهزة بخطوة وحدة).
- الإيقونات مرسومة بإيد (رجفة خفيفة)، والخط Rubik جوّا البرنامج.
- المايكروفون مأجّل لمرحلة الصوت، وزر «إعداداتي» مخفي لحد ما يجي برنامج الإعدادات (0.12).

جرّب بإيدك: كبسة يمين عليها؛ Ctrl+Alt+S من أي برنامج؛ اكتب «مؤقت ٥»؛ ادخل على «بسكت شوي» واختار «نص ساعة»؛
اكبس كبستين عليها وتأكد إنها ما فتحت القائمة؛ افتح القائمة وهي بزاوية الشاشة.
بدنا رأيك: ألوان رأس البطاقة لكل مزاج — شوف docs/design/menu/header-tints.png (الهادي متل ما اعتمدناه؛ الباقي اقتراح).

من 0.10.1 — صارت لطيفة مع شريط المهام 🧩
-------------------------------------------------
- قبل كانت سَحَر ترسم على نافذة شفافة قد الشاشة كلها، فالويندوز وبرامج شريط المهام (متل Now Bar) كانوا يحسبوها
  برنامج ملء شاشة: Now Bar كان يختفي، والإشعارات ممكن تسكت. هلق نافذتها قدّها هي وبتمشي معها،
  وبتكبر بس وقت يلزم (القائمة، الألعاب، العرض، الفراشة) — ودايماً أصغر من الشاشة ولو ببكسل.
- بتختفي بس إذا برنامج ملء الشاشة على نفس الشاشة يلي هي عليها. على الشاشات التانية بتضل، بس ساكتة (بلا تعليقات).
  إذا انتقلت لشاشة فيها ملء شاشة بتختفي، ولما يخلص بترجع. السجل بيكتب رقم الشاشة (مو اسم البرنامج).

جرّب بإيدك (ما منقدر نجربها عالسيرفر):
  ١. شغّل Now Bar مع سَحَر: اتركها ترتاح، افتح قائمتها، العب «الحقني» — حبة Now Bar لازم تضل ظاهرة.
  ٢. خلّي إشعار يطلع وهي عالشاشة (مثلاً مؤقّت من تطبيق الساعة) — لازم يبيّن.
  ٣. إذا عندك شاشتين: شغّل فيديو ملء شاشة عالشاشة التانية — لازم تضل ظاهرة؛ وعلى شاشتها — لازم تختفي.
  ٤. افتح قائمتها وهي بزاوية الشاشة، وخلّيها تحكي، والعب «الطابة» — ما لازم ينقص شي من رسمها.

من 0.10.0 — شكلها الجديد وروحها ✨
-------------------------------------------
- شكلها الأصلي كلو باقي، ومعو:
  🌟 النجوم يلي حواليها صار إلها حافة دافية، فبتبيّن حتى فوق الشبابيك البيضا.
  🌙 سما صغيرة جواتها بأسفلها: لون ليلي ناعم مع ست نجمات بيضا بتلمع ونقاط صغيرة. لونها بيتغيّر مع مزاجها.
  😊 وجه فرحان: عيون متل الهلال وضحكة كبيرة وخدود أحمر شوي، لما تنط أو تكبس عليها.
- روحها: كلها بتتحرك متل كائن حي وبالكاد بتصرف من الجهاز —
  بتطوف لفوق ولتحت، بتتنفّس، بتميل شوي، ورقتها بتتأخر عنها وبترجف بعد النطّة،
  ولما تكبس عليها بتنط نطّة صغيرة (بتنضغط، بتطلع، بتنزل وبترجّ)، وكل كم ثانية بتنط لحالها أو بتطّلع عالجنب.
  عيونها بتلحق الماوس إذا قريب (لـ 400 نقطة) وجسمها بيميل شوي لعندو. القمر بيلف حواليها كل 16 ثانية.
- إذا "تأثيرات الحركة" (Animation effects) طافية بالويندوز: بتوقف كل الحركة وبتضل هادية، والقمر بيرتاح جنبها.
- 🏃 الحقني صارت بتنمسك: بتتعب مع الوقت، بعد كل هربة بترتاح شوي، ونطّاتها بتقصر. عادة بتمسكها خلال 10–20 ثانية.
- لما تكون بعيد عن الجهاز (ما في ماوس ولا كيبورد 5 دقايق) ما بتعمل حركات لحالها، حتى لو في أغنية شغالة.
- السجل (engine.log) صار بيكتب: إطفاء الجهاز أو تسجيل الخروج، النوم والصحيان، إذا المرة الماضية سكّرت بلا خروج طبيعي،
  وليش اختفت أو رجعت فوق ملء الشاشة. و"life: waiting" ما عاد يتكرر كل شوي.
- الإعدادات ما بتنحفظ فوق ملف خربان: بيضل نسخة .bad والملف ما بينمسح.

شو جرّب: اكبس عليها؛ قرّب الماوس منها وبعّدو؛ العب «الحقني»؛ طفّي "تأثيرات الحركة" من إعدادات الويندوز
(إمكانية الوصول ← التأثيرات المرئية) وشوف كيف بتهدى؛ اتركها شوي وشوفها كيف بتنط لحالها.
بدنا رأيك بشغلتين: (١) علو النطّة — شوف صورة hop-strip.png (8 لحظات من نطّة وحدة)، و(٢) ألوان السما جواتها بكل مزاج —
شوف 06-moods-sheet.png. الهادي (Calm) متل ما اعتمدناه؛ الـ 15 التانيين اقتراح.

من 0.9.0 — الألعاب 🎮
-------------------------------
"العبي" بالقائمة صار فيها:
- 🏃 الحقني: 45 ثانية، بتهرب من الماوس، وكل ما تكبس عليها بتمسكها نقطة.
- 🙈 غمّضلي: بتتخبّى بطرف الشاشة وبيبيّن شي صغير منها، وبعد 20 ثانية بتلمعلك. 3 جولات، وبتقلك بكم ثانية لقيتها بالمعدل.
- ⚽ الطابة: امسك الطابة بالماوس واشلفها، بتنط بالشاشة، وسحورة بتجيبها وبترجّعلك ياها (5 مرات).
- ⭐ لقط النجوم: 40 ثانية، النجوم بتنزل وسحورة بتلحق الماوس تحت لتلقطهن.
- 🎲 حركة عشوائية (متل "العبي" القديمة، مع الحركات الجديدة).
وقت اللعبة الماوس للعبة بس: Esc أو كبسة يمين بتوقفها، وبالآخر بتقلك النتيجة.

من 0.8.0 — ردّات فعل وحركات حيّة
--------------------------------
- إذا سحبتها وهزّيتها يمين وشمال بسرعة:
  😵 بتدوخ (عيون دوّامات ونجوم حول راسها)، أو 🤢 إذا الهزّة قوية بتستفرغ بشكل كرتوني وبعدين بتستحي،
  أو 😠 بتعصّب (حمرا وبخار) وبتزعل 20 ثانية: بتبعد عن الماوس، وإذا كبست عليها بتقلك "هِم! 😤"،
  وتالت نكزة لطيفة بتصالحك 💜. إذا هزّيتها مرة تانية خلال 5 دقايق، غالباً بتعصّب.
- حركات حيّة بدل الطيران بس: بتمشي على التاسك بار وبتقعد ترتاح، بتتمطّى متل القطة، بتتثاوب،
  بتحك راسها، بتقعد جنب الشباك يلي فاتحه وبتتفرّج، بتنط على طول الشباك، بترمي قمرها وبتلقطه،
  وبتلحق فراشة صغيرة.
- لما تبلّش تكتب، أحياناً بتقعد جنب شباكك (مش فوقه) وبتعمل حالها عم تكتب معك، وبتوقف لما توقف.

شو جرّب: اسحبها وهزّها؛ هزّها مرة تانية بعد شوي؛ "فرجيني حركاتك" من أيقونتها لتشوف الحركات الجديدة (26–36).

من 0.7.0
--------
النسخة النضيفة
---------------
- أول ما تشغّلها بتمسح كل شي خلّفته النسخ القديمة (الإعدادات، الملاحظات، الحافظة، السجل) وبتبلّش من الصفر. مرة وحدة بس.
  بعد التشغيل احذف مجلدات النسخ القديمة يلي فكّيتها بإيدك.
- تعارف: بتسلّم عليك وبتسألك شو بدها تناديك، وإذا انت شب ولا بنت. كلامها معك بيصير حسب جوابك ("اكتب" / "اكتبي")،
  وبتقلك "صباح الخير" باسمك. اسمك ما بينكتب بالسجل.
- تذكير الصلاة رجع قبل 10 دقايق (بـ 0.6.0 كان يطلع قبل دقيقة إذا عندك إعدادات قديمة).
- ما عادت تختفي فوق المتصفح المكبّر (كانت تحسبه ملء شاشة إذا شريط المهام مخفي تلقائياً). بتختفي بس مع ملء الشاشة الحقيقي،
  والسجل بيكتب نوع الشباك يلي اختفت فوقه لنعرف السبب إذا صارت مرة تانية.

من 0.6.0
--------
- 🕌 أوقات الصلاة بالقائمة: أول مرة بتسألك عن مدينتك، وبعدين بتفرجيك الصلاة الجاية وقديش باقي، وأوقات اليوم الست.
  بتنحسب على جهازك بلا نت، على توقيت الجهاز. طريقة الحساب بتختارها من السطر "طريقة الحساب" (أم القرى افتراضياً،
  رابطة العالم الإسلامي، المصرية، كراتشي، أمريكا الشمالية). "المدينة: … — غيّر" لتغيّر المدينة.
  قبل كل صلاة بـ 10 دقايق بتذكّرك (وجّ التذكير، بتلفت انتباهك، وبتقول "بعد 10 دقايق العصر" مثلاً).
  وقت الأذان بتهدى 5 دقايق: لا تعليقات ولا حركات كبيرة. ما في صوت أذان بهالنسخة.
  اسم المدينة بيروح لـ Open-Meteo مرة وحدة لنعرف مكانها، وما بينكتب بالسجل.
- 🎧 الاستماع بالقائمة:
  🎵 موسيقى: ⏮️ السابق، ⏯️ تشغيل/إيقاف، ⏭️ التالي لأي برنامج عم يشغّل (سبوتيفاي، يوتيوب…)، وفقاعتها بتقول شو عم يشتغل.
  📖 قرآن: 114 سورة (آخر وحدة سمعتها أول شي) و6 قرّاء (العفاسي، عبد الباسط، الحصري، المنشاوي، المعيقلي، السديس).
  وقت تشغّل القرآن بتوقّف الموسيقى، وبتلبس السماعات وبتغمض عيونها وبتهدى: لا رقص ولا تعليقات. لتوقفه: 🎧 ← ⏹️ وقّف القرآن.
  التلاوات من islamic.network (alquran.cloud)، ومسموحة للاستماع الشخصي. إذا ما في نت بتقلك "ما قدرت شغّل التلاوة هلق".

من 0.5.0
--------
- 📋 الحافظة بالقائمة: آخر 20 شي نسخته (نص، رابط، صورة). كبسة على وحدة بترجّعها للحافظة، و"امسح الكل" بيمسح.
  ما بتحفظ شي من برامج كلمات السر يلي على الجهاز، ولا شي معلّم إنو خاص. وإذا نسخت شي بيشبه كلمة سر من المتصفح
  (متل إضافات كلمات السر)، بتبيّنه "🔒 ••••••" وما بتكتبه على الجهاز أبداً. كل شي بيضل على جهازك.
- بتعرف وقت يخلص تنزيل ملف، ووقت تاخد لقطة شاشة (Print Screen أو Win+Shift+S)، ووقت الجهاز يضل مشغول كتير دقيقتين.
- إذا الكاميرا أو المايك شغالين (مكالمة فيديو) بتهدى: ما بتعلّق وما بتعمل حركات كبيرة.
- بتعلّق بلطف أحياناً: إذا صار لك ساعة أو ساعتين على نفس البرنامج، أو صباح الخير أول النهار.
  مرة كل 45 دقيقة بالكتير، وإذا كبست عليها لتسكّر التعليق بتعلّق أقل. بتعرف اسم البرنامج بس، ما بتقرا شي من الشاشة.
- المطر (إذا بدك): كبسة يمين على أيقونتها ← "الطقس…" واكتب مدينتك. إذا في مطر جاي بالساعتين الجايين بتنبهك.
  اسم المدينة بيروح لـ Open-Meteo مرة وحدة، وبعدين بس الإحداثيات، مرة بالساعة.

من 0.4.0
--------
- القائمة الدائرية: كليك يمين أو كبستين عليها. فيها 6 أزرار:
  ⏱️ مؤقت (5 / 15 / 25 / 45 دقيقة أو وقت بتكتبه، مع حلقة وقت حواليها، ولما يخلص بتلفت انتباهك. كبسة عليها بتسكّت المؤقت).
  📝 ملاحظة سريعة (بتكتب وبتكبس Enter، وبتشوف آخر 3 ملاحظات).
  🌗 مستوى نشاطها. 🔕 لا تزعجني (نص ساعة، ساعة، ساعتين، أو لحد ما توقّفها). 😴 خبّيها ساعة. 🎲 العبي (حركة عشوائية).
  بتتسكّر بـ Esc أو بكبسة برّا. إذا كانت جنب حافة الشاشة بتصير نص دايرة.
- الحركات الـ25 كلها صارت موجودة. الجديدة: بتلحق الماوس، بتختبي ورا نافذة وبتطل، بتستحي إذا قرّبت الماوس كتير،
  بتدخل وبتسلّم أول ما تشغّل الجهاز، بتودّعك لما تسكّرها، بتنط على التاسك بار، بتلفت انتباهك بالأشياء المهمة،
  وبتغفى على أيقونة.
- "فرجيني حركاتك": كبسة يمين على أيقونتها ← "فرجيني حركاتك". بتعرض كل حركاتها ووجوهها ورا بعض، وتحتها اسم كل وحدة.
  كبسة عليها بتوقف العرض.

من 0.3.0 (لسا موجودين)
----------------------
- 10 طيرات مختلفة، وحركات: بتلعب مع قمرها، فضولية، بترقص مع الموسيقى، بتتنفّس، بتلف حوالين الماوس، ولحظات وجه صغيرة.
- أغنية من يوتيوب: سماعات. والمتصفح قدامك: ما بتطير فوق الفيديو.
- "افتح مجلد سَحَر" من قائمة أيقونتها: فيه ملف السجل engine.log لتبعتلي ياه.

من 0.2.0 (لسا موجودين)
----------------------
- بتعيش لحالها، وعيونها بتلحق الماوس.
- نشاطها: كبسة يمين على أيقونتها ← "نشاطها": هادية (كل 5–10 دقايق)، متوسطة (كل 1–3 دقايق)، نشيطة (كل نص دقيقة لدقيقة ونص).
- ما بتزعجك: وقت تكتب بتوقف وبتبعد عن مكان الكتابة. ووقت تحضر فيديو بتهدى.
- بتتعلّم ذوقك: إذا كبست عليها بعد حركة بتعملها أكتر، وإذا شلتها من نصها بتخففها.
- بتعرف روتينك، بتذكّرك بالاستراحة بعد ساعتين، وبعد نص الليل بتصير غامضة.
- إذا غبت 5 دقايق بتنام بالزاوية، ولما ترجع بتصحى.
- لترجّع تعلّمها من الأول: سكّرها وامسح الملف %AppData%\Sahar\life.json

كيف تثبّتها
-----------
1. فك الضغط عن الملف Sahar-0.12.0-test-win-x64.zip بأي مجلد.
2. شغّل Sahar.Engine.exe — سَحَر بتطلع تحت عاليمين، فوق التاسك بار.
   إذا طلع تحذير ويندوز (SmartScreen): "More info" ← "Run anyway". النسخة مش موقّعة لسا.
3. للإغلاق: كبسة يمين على أيقونتها جنب الساعة (يمكن تكون مخبّاية تحت السهم ^) ← "إغلاق / Quit".
1. نزّل Sahar-Setup-0.11.0.exe من صفحة النسخة على GitHub وشغّلو.
2. الويندوز رح يقلك «Windows protected your PC» (هاد SmartScreen) لأن النسخة مش موقّعة لسا. طبيعي:
   كبّس على «More info» (معلومات إضافية) ← بيطلع زر «Run anyway» (تشغيل على أي حال) ← كبّس عليه.
   هالرسالة بتطلع أول مرة بس؛ التحديثات من جوّا سَحَر ما بتطلّعها.
3. اختار إذا بدك ياها تشتغل مع الويندوز ← «التالي» ← «إنهاء». سَحَر بتطلع تحت عاليمين، فوق التاسك بار.
   بدك النسخة المحمولة بلا تثبيت؟ فك الضغط عن Sahar-0.11.0-test-win-x64.zip وشغّل Sahar.Engine.exe (نفس تحذير SmartScreen).
   إذا كنت مشغّلها من الـ zip: المثبّت بيسكّرها لحالو؛ بعد التثبيت احذف المجلد القديم.
4. للإغلاق: كبسة يمين على أيقونتها جنب الساعة (يمكن تكون مخبّاية تحت السهم ^) ← "إغلاق / Quit".
   "إخفاء ساعة" بيخبّيها ساعة وبترجع لحالها.

شو تجرّب
---------
- كبسة عليها: بتنضغط وبتضحك.
- اسحبها بالماوس: بتتعلّق من ورقتها وبتتأرجح. إذا رميتها بسرعة بتطير وبتتدحرج وبتوقف جوّا الشاشة.
- خلّي الماوس فوقها شوي: بتميل لعندك.
- شغّل فيديو أو لعبة على كامل الشاشة: لازم تختفي، وترجع لما تطلع. نافذة مكبّرة عادية ما لازم تخبّيها.
- افصل النت ورجّعه، وافصل الشاحن ورجّعه (عاللابتوب): لازم يتغيّر وجّها.
- اتركها 5 دقايق بلا ماوس ولا كيبورد: بتنام، ولما ترجع بتصحى.
- قفل الجهاز (Win+L) وافتحه: نفس الشي.
- لقطة شاشة: سَحَر ما بتطلع بلقطات الشاشة، وهاد مقصود.

فحوصات يدوية مهمة (ما بتنجرّب على GitHub)
-----------------------------------------
- شاشتين بتكبير مختلف (مثلاً اللابتوب 150% والشاشة 100%): اسحبها من وحدة للتانية. لازم يضل حجمها نفسه، تكون واضحة مش مغبّشة، وما ترجف عالحدود.
- حطها عالشاشة الخارجية وافصلها (أو غيّر الدقة): لازم ترجع لحالها عالشاشة الباقية.
- التاسك بار عاليسار أو فوق، أو مخفي تلقائياً: "تقعد" و"تطل" لازم يستعملوا حافته الحقيقية.
- Win+Tab و Alt+Tab: ما لازم تختفي.
- لعبة أو فيديو بكامل الشاشة: بتختفي كلياً (ولا حتى نقطة)، وبترجع لمكانها لما تطلع.
- نيّم الجهاز وصحّيه، وحدّث تعريف كرت الشاشة وهي شغالة، وادخل بـ Remote Desktop واطلع: لازم تضل مرسومة.
- سكّر Explorer وافتحه (من مدير المهام): أيقونتها لازم ترجع جنب الساعة. كبسة عالأيقونة بترجّعها إذا كانت مخبّاية.
- لقطة شاشة عادية: ما لازم تطلع فيها.
- إشعارات ويندوز لازم تضل تطلع وسَحَر شغالة.
- اكتب بالمتصفح (مثلاً بحث بجوجل): لازم توقف حركتها وتبعد إذا كانت قريبة.
- شغّل أغنية بسبوتيفاي: لازم تلبس السماعات وترقص أحياناً.
- شغّل أغنية بيوتيوب والمتصفح قدامك: سماعات وبس، بلا طيران. روح لنافذة تانية: بترجع تتحرك وترقص. وقّف الأغنية: بتوقف رقص.
- خلّيها على "نشيطة" ربع ساعة: لازم تشوف حركات كتير مختلفة، مش بس طيران.
- كبستين على التاتش باد، وكليك يمين: لازم تفتح القائمة. جرّبها وهي بكل زاوية من الشاشة: كل الأزرار لازم تبيّن.
- حط مؤقت 5 دقايق ونيّم الجهاز 10 دقايق: لما يصحى لازم يخلص المؤقت فوراً.
- اكتب ملاحظة بالعربي (بلغة الكيبورد العربي): لازم تنحفظ وتبيّن صح.
- سكّرها من أيقونتها: لازم تودّعك وتتحوّل لنجوم.
- انسخ كلمة سر من برنامج كلمات السر (Bitwarden أو 1Password…) أو من إضافته بالمتصفح: ما لازم تطلع مكشوفة بالحافظة تبعها.
- نزّل ملف حقيقي من المتصفح، وخود لقطة بـ Win+Shift+S، وافتح مكالمة فيديو: لازم تتفاعل متل ما هو مكتوب فوق.
- شغّل الطقس بمدينتك.
- "لا تزعجني" تبع ويندوز (Focus Assist) ما إلو طريقة رسمية لنعرفه، فاستعمل "لا تزعجني" تبعها من قائمتها.
- بعد ساعتين شغل متواصل: لازم يطلع وجّ الاستراحة ☕.

- 🕌 قارن أوقات مدينتك مع جامعك، واستنى تذكير صلاة حقيقي (10 دقايق قبل).
- 🎵 شغّل أغنية بسبوتيفاي وجرّب ⏮️ ⏯️ ⏭️ من قائمتها، وشوف إذا الفقاعة بتقول اسم الأغنية.
- 📖 شغّل سورة والسماعات بأذنيك وأغنية شغالة: لازم توقف الأغنية وتبلش التلاوة، وسَحَر تهدى. وبآخر السورة بترجع عادي.

بهالنسخة لسا ما في: الأصوات وربط Claude. هدول بالخطط الجاية.

إذا صار شي غلط
--------------
ابعتلي:
- شو عملت وشو صار (ولقطة شاشة أو فيديو قصير إذا قدرت).
- ملف السجل engine.log: كبسة يمين على أيقونتها ← "افتح مجلد سَحَر"، وابعتلي الملف.
  (أو الصق %LocalAppData%\Sahar بشريط العنوان بمستكشف الملفات.)


Sahar — test build 0.12.0 (Windows)
==================================

What's new in 0.12.0 — «إعداداتي»: her own settings app ⚙️
-----------------------------------------------
- «إعداداتي» in her menu (and in her tray icon's menu) opens a quiet settings window in her colours, light or dark like
  Windows. Every change applies at once, no Save button. Only one window: opening it again brings the open one to the
  front; closing it ends it completely. It never goes online: your city is searched through Sahar herself.
- Pages: Sahar · Menu / Prayer · Listening / General · Privacy / About. (Sounds and Claude Code stay hidden until they work.)
- Sahar: a small living Sahar on the page hops and says a word on every change. Your name, guy or girl (her words follow
  you), her size (small / medium / large — she resizes on screen at once), how lively she is, and what she does by
  herself: comment on your work, change mood by herself, play by herself, feel the weather — and the stars inside her.
- Menu: what her menu shows (each item on or off), what shows at the top, what opens it and where, and the keyboard
  shortcut (press «غيّر», then the new keys; one that is taken or common is refused with the reason, the old one stays).
- Prayer: today's five times and the next with a countdown, the city or Windows location, the method, Asr, the reminder.
- Listening: the reciter. General: light / dark / like Windows, and the language (Arabic or English — the settings only, for now).
- Privacy: clear what you copied, the weather, and «امسح كل شي عني» (she forgets everything and gets to know you again).
- If her settings file is damaged and can't be read: a calm note above every page offers «نبلّش من جديد». She asks «أكيد؟»,
  then goes back to her first settings and asks your name again. The damaged file is kept aside (.bad); your notes stay.
- Her size from the settings now really applies (before, she was always medium).
- The zip is about 30 MB bigger: the settings app is in the Sahar.Settings folder next to Sahar.Engine.exe — keep them
  together. It needs Microsoft "WebView2" (in Windows 11 and most of Windows 10); without it «إعداداتي» does not show.

What to try: open «إعداداتي» and change her size while watching her; turn «نلعب؟» off on the Menu page and open her
menu; change the shortcut to Ctrl+Alt+K and try it; switch Windows to dark and look at the window; close it and check
Task Manager that it is gone.

From 0.11.0 — her new menu and a chat box 💬
New in the next version — an installer, and updates she asks you about 📦
-----------------------------------------------
- An installer: Sahar-Setup-0.11.0.exe. Run it once; nothing to unzip. It installs for you only (no admin) into
  %LocalAppData%\Programs\Sahar, puts «سحر» in the Start menu and asks whether she starts with Windows (ticked).
- When a new version is out she says so in her bubble («في نسخة جديدة مني …، بدك ثبّتها؟») with yes and later.
  Yes: she downloads it, checks it is ours (a digital signature) and exactly that file; the installer closes her,
  installs it and starts her again a few seconds later. If the install does not work out, she is back as she was and
  tells you. Running from the zip: you get the installer's wizard (you see where she goes and choose start-with-
  Windows), and afterwards she tells you where she lives now. Later (or closing the bubble): she asks again the next
  day. She never updates without a yes.
- She looks 2 minutes after she starts, then every 12 hours; by hand: right-click her icon → «دوّر على تحديث».
- Your settings, notes and her log stay as they are through an update or a reinstall.
- Uninstall: Windows Settings → Apps → سَحَر → Uninstall. It asks whether to delete her settings too (default: no).
- She has an icon now: her face 🌱 (Start menu, by the clock, Settings → Apps). See docs/design/icon/icon-sheet.png
- An older setup over a newer Sahar asks before replacing her.
- Network: only github.com (the release's files); nothing about you but her name and version.
  No internet: she stays quiet and tries later (one line in the log).

What's new in 0.11.0 — her new menu and a chat box 💬
-----------------------------------------------
- The radial ring is gone. A right-click on her (or Ctrl+Alt+S from anywhere) opens a quiet card beside her: her
  line «شو بدك نعمل؟», under it the one most urgent thing (a running timer, the next prayer, the Quran, or how long
  she stays quiet), a chat box, her three tiles (play · hush · nap) and four tools (timer · note · listen · clipboard).
  While it is open her moon rests on the card's corner next to her.
- A double-click no longer opens the menu: it is two pokes (a hop and a laugh), as asked.
- Keyboard: Tab and the arrows move, 1–3 pick the tiles, Enter opens, Esc goes back then closes; focus returns to
  the window you were in.
- The chat box (local, no AI yet) understands timers, notes, hush, sleep, games, her moves (dance, hop, stretch),
  prayer and greetings, in Arabic or English, with any digits. She answers in her line and hops. The log keeps the
  kind of command only (e.g. "chat: Timer"), never what you typed.
- Her face now changes like a blink: the eyes close, the face swaps while they are shut, the eyes open — never two
  faces at once. A poke is one thing: a hop with her «^ ^» face (no pink "poked" face first).
- Opening the menu or starting a game no longer draws her in the wrong place for a moment (her drawing moves to a
  ready spare window in one step).
- Hand-drawn icons (a slight wobble) and the Rubik font inside the app.
- The mic waits for the voice phase; the «إعداداتي» button stays hidden until the settings app arrives (0.12).

Please check by hand: right-click her; press Ctrl+Alt+S in another app; type «مؤقت ٥»; open «بسكت شوي» and pick
«نص ساعة»; double-click her and see that the menu stays closed; open the menu with her in a screen corner.
We'd like your view on the card's header colour per mood: docs/design/menu/header-tints.png (Calm as approved;
the others are a proposal).

From 0.10.1 — friendly to the taskbar 🧩
-----------------------------------------------
- Sahar used to draw on a transparent window as big as the whole screen, so Windows and taskbar tools (such as
  Now Bar) took her for a fullscreen app: Now Bar hid, and notifications could go quiet. Now her window is her size
  and moves with her; it grows only while something needs it (her menu, a game, her showcase, the butterfly) and is
  always at least a pixel short of a whole screen.
- She hides only when the fullscreen app is on the screen she is on. On other screens she stays, quietly (no
  comments). Moved onto a screen with a fullscreen app she hides, and comes back when it ends. The log names the
  screen by number (never the app's title).

Please check by hand (CI cannot):
  1. Run Now Bar with Sahar: leave her resting, open her menu, play the chase — Now Bar's pill must stay.
  2. Let a notification appear while she is on screen (e.g. a timer in the Clock app) — it must show.
  3. With two screens: a fullscreen video on the other screen — she stays; on her screen — she hides.
  4. Open her menu with her in a corner, let her talk, play the ball game — nothing of her drawing is cut off.

From 0.10.0 — her new look and her soul ✨
-------------------------------------------------
- All of her original look stays, plus: a warm edge on her stars (they read on white windows), a little night sky
  inside her lower body with six twinkling white stars and dots (it takes her mood's colour), and a happy face
  (crescent eyes, a big smile, warmer cheeks) when she hops or you tap her.
- Her soul: she floats, breathes, sways a little, her leaf lags behind and wobbles after a hop; a tap makes her hop
  (squash, rise, land with a jiggle); every few seconds she hops or glances aside by herself; her eyes follow the
  mouse within 400 px and she leans toward it. The moon laps her every 16 s. All of it runs on the compositor.
- With Windows "Animation effects" off she rests completely; the moon rests beside her.
- 🏃 The chase is catchable: she tires, rests after each dart, and her darts get shorter — usually caught in 10–20 s.
- Away (no mouse or keyboard for 5 minutes) she does nothing by herself, even with a song playing.
- engine.log now records shutdown/sign-out, sleep/resume, a previous run that ended without exit, and why she hid
  or came back over a fullscreen app; "life: waiting" no longer repeats.
- Settings are never saved over an unreadable file (a .bad copy is kept).

Try: tap her; bring the mouse near and away; play the chase; turn off Windows "Animation effects"
(Accessibility → Visual effects) and watch her rest; leave her alone a while and see her hop by herself.
Two things we need your word on: (1) the hop height — see hop-strip.png (eight moments of one hop), and
(2) the night colours per mood — see 06-moods-sheet.png. Calm is the approved one; the other 15 are a proposal.

From 0.9.0 — games 🎮
-----------------------------
"العبي" in her menu now offers: 🏃 chase (45 s, she runs from the pointer, a click catches her), 🙈 hide and seek
(she hides at an edge with a bit showing, a hint after 20 s, 3 rounds, your average time), ⚽ ball (grab and flick it,
it bounces, she fetches it back, 5 times), ⭐ star catch (40 s, she follows the pointer along the bottom), and 🎲 a
random move. During a game the mouse is the game's: Esc or a right-click stops it; she tells you the score.

From 0.8.0 — reactions and living moves
---------------------------------------
- Drag her and shake her left and right quickly: 😵 she gets dizzy (spiral eyes, stars), 🤢 after a hard shake she is
  sick in a cartoon way and then a little embarrassed, or 😠 she gets angry (red, steam) and sulks for 20 s: she keeps
  away from the pointer, a click gets "هِم! 😤", and the third gentle poke makes up 💜. Shake her again within 5 minutes
  and she is likely to get angry.
- Living moves instead of mostly flying: she walks on the taskbar and sits, stretches like a cat, yawns, scratches her
  head, sits beside your window and watches, hops along a window, tosses her moon and catches it, chases a butterfly.
- When you start typing she sometimes sits beside your window (never over it) and pretends to type along.

Try: drag and shake her; shake her again a bit later; "Show me your moves" from her tray icon for the new moves (26–36).

From 0.7.0 — the clean version
------------------------------
- Its first start removes everything older test builds left (settings, notes, clipboard, log) and starts fresh, once.
  Delete the folders of old builds you unzipped yourself.
- Hello: she greets you and asks what to call you and whether you are a man or a woman; her Arabic then addresses you
  the right way ("اكتب" / "اكتبي") and her good morning uses your name. Your name never reaches the log.
- The prayer reminder is 10 minutes before again (0.6.0 gave it 1 minute before with an older settings file).
- She no longer vanishes over a maximized browser (it counted as fullscreen with an auto-hidden taskbar); she hides only
  for real fullscreen, and the log names the window class she hid over.

From 0.6.0
----------
- 🕌 Prayer times in her menu: the first time she asks your city, then shows the next prayer, the time left, and today's six times.
  Computed on your PC, offline, in your PC's time zone. Pick the method from the "طريقة الحساب" line (Umm al-Qura by default,
  Muslim World League, Egyptian, Karachi, North America); "المدينة: … — غيّر" changes the city.
  10 minutes before each prayer she reminds you (the reminder face, she draws your eye, and says e.g. "بعد 10 دقايق العصر").
  At the adhan she stays quiet for 5 minutes: no comments, no big moves. No adhan sound in this build.
  Your city goes to Open-Meteo once to find where it is, and never reaches the log.
- 🎧 Listening in her menu:
  🎵 Music: ⏮️ previous, ⏯️ play/pause, ⏭️ next for whatever app plays (Spotify, YouTube…); her bubble says what is playing.
  📖 Quran: 114 surahs (the last one you heard first) and 6 reciters (Alafasy, Abdul Basit, Al-Husary, Al-Minshawi, Maher
  Al-Muaiqly, As-Sudais). Starting it pauses your music; she wears headphones, closes her eyes and goes quiet: no dance, no
  comments. To stop: 🎧 → ⏹️ وقّف القرآن. Recitations stream from islamic.network (alquran.cloud), free for personal listening.
  With no internet she says "ما قدرت شغّل التلاوة هلق".

From 0.5.0
----------
- 📋 Clipboard in her menu: the last 20 things you copied (text, links, images). Click one to copy it again; "Clear all".
  Nothing from password-manager apps or content marked private is kept. A password-looking copy from a browser (password
  extensions) shows as "🔒 ••••••" and is never written to disk. Everything stays on your PC.
- She notices a finished download, a screenshot (Print Screen or Win+Shift+S) and a CPU busy for two minutes.
- Camera or microphone on (a video call): she goes quiet — no comments, no big moves.
- Gentle comments now and then: an hour or two on the same app, or good morning. At most every 45 minutes, and less
  if you click them away. She only knows the app's name; she reads nothing on your screen.
- Rain (if you want it): right-click her icon → "Weather…" and type your city. Rain within two hours: she tells you.
  Your city goes to Open-Meteo once, then only its coordinates, once an hour.

From 0.4.0
----------
- The radial menu: right-click or double-click her. Six buttons:
  ⏱️ Timer (5 / 15 / 25 / 45 min or a time you type; a ring of time around her; when it ends she draws your eye, a click on her dismisses it).
  📝 Quick note (type, press Enter; the last 3 notes show under it).
  🌗 Activity. 🔕 Do not disturb (30 min, 1 h, 2 h, or until you turn it off). 😴 Hide 1 h. 🎲 Play (a random move).
  Esc or a click outside closes it; near a screen edge it becomes a half circle.
- All 25 moves now. New: she follows the pointer, hides behind a window and peeks, gets shy if the pointer keeps coming close,
  flies in and says hello when the PC starts, waves goodbye when you quit her, hops along the taskbar, draws your eye for
  important things, and naps on an icon.
- "Show me your moves": right-click her icon → "Show me your moves". She plays every move and face, with its name under her.
  A click on her stops the show.

Still here from 0.3.0
---------------------
- 10 different flights; plays with her moon, gets curious, dances to music, breathes, circles the pointer, small face moments.
- A song on YouTube: headphones; with the browser in front she does not fly over the video.
- "Open Sahar folder" in her icon's menu: engine.log is there to send.

Still here from 0.2.0
---------------------
- She lives on her own, and her eyes follow the mouse.
- Activity: right-click her icon → "نشاطها / Activity": Calm (every 5–10 min), Medium (1–3 min), Lively (30–90 s).
- She stays out of your way while you type, and calms down while you watch a video.
- She learns what you like (poke = more, grab mid-move = less) and your routine; break reminder after two hours; mystic after midnight.
- Away for 5 minutes: she naps in the corner and wakes when you are back.
- To reset what she learned: quit her and delete %AppData%\Sahar\life.json

Run it
------
1. Unzip Sahar-0.12.0-test-win-x64.zip anywhere.
2. Run Sahar.Engine.exe. Sahar appears bottom-right, above the taskbar.
   If Windows SmartScreen warns you: "More info" → "Run anyway" (the build is not signed yet).
3. To quit: right-click her icon next to the clock (it may be under the ^ arrow) → "إغلاق / Quit".
Install it
----------
1. Download Sahar-Setup-0.11.0.exe from the release page on GitHub and run it.
2. Windows SmartScreen says "Windows protected your PC" because the build is not signed yet. That is expected:
   click "More info", then "Run anyway". Only the first time; Sahar's own updates do not show it.
3. Choose whether she starts with Windows → Next → Finish. Sahar appears bottom-right, above the taskbar.
   Portable instead: unzip Sahar-0.11.0-test-win-x64.zip and run Sahar.Engine.exe (same SmartScreen warning).
   Running her from the zip: the installer closes her for you; delete the old folder afterwards.
4. To quit: right-click her icon next to the clock (it may be under the ^ arrow) → "إغلاق / Quit".
   "Hide 1 h" hides her for an hour.

What to try
-----------
- Click her: she squishes and giggles.
- Drag her: she hangs from her leaf and swings. Throw her fast: she flies, spins and stops inside the screen.
- Rest the cursor on her: she leans toward it.
- Play a fullscreen video or game: she should hide, and come back after. A normal maximized window must not hide her.
- Turn the network off and on, unplug and replug the charger (laptop): her face should change.
- Leave the computer alone for 5 minutes: she falls asleep and wakes when you return. Same with Win+L and unlock.
- Screenshots: she does not appear in them, on purpose.

Manual checks (GitHub cannot test these)
-----------------------------------------
- Two monitors with different scaling (e.g. laptop 150 %, monitor 100 %): drag her across. Same size, crisp, no flicker at the border.
- Put her on the external monitor, then unplug it (or change resolution): she must come back onto the remaining screen.
- Taskbar on the left or top, or auto-hidden: sitting and peeking use its real edge.
- Win+Tab and Alt+Tab must not hide her.
- Fullscreen game or video: she disappears completely (not even a dot) and returns to the same spot after.
- Sleep/resume, a graphics-driver update while she runs, Remote Desktop in and out: she stays drawn.
- Restart Explorer (Task Manager): her tray icon comes back. Clicking the icon brings her back if hidden.
- Ordinary screenshots must not show her.
- Windows notifications must still appear while she runs.
- Type in a browser (e.g. a Google search): she stops moving and steps away if she is close.
- Play a song in Spotify: headphones on, and she dances now and then.
- Play a song on YouTube with the browser in front: headphones only, no flights. Switch to another window: she moves and dances again. Stop the song: the dance stops.
- Leave her on Lively for 15 minutes: you should see many different moves, not just flights.
- Double-click on a touchpad, and right-click: the menu opens. Try it with her in every corner: every button shows.
- Start a 5-minute timer and sleep the PC for 10 minutes: when it wakes, the timer ends right away.
- Write a note in Arabic (Arabic keyboard layout): it is saved and shows correctly.
- Quit her from her icon: she waves goodbye and turns into stars.
- Copy a password from a password manager app or its browser extension: it must never appear in clear in her list.
- Download a real file in a browser, take a Win+Shift+S snip, start a video call: she reacts as described above.
- Turn on the weather for your city.
- Windows' own "Do not disturb" (Focus Assist) has no official way to be read; use her own "Do not disturb" in her menu.
- After two hours of non-stop work: the break face ☕.

- 🕌 Compare your city's times with your mosque, and wait for a real prayer reminder (10 minutes before).
- 🎵 Play a song in Spotify and try ⏮️ ⏯️ ⏭️ from her menu; the bubble should name the song.
- 📖 Play a surah with headphones on while a song plays: the song pauses, the recitation starts, Sahar goes quiet,
  and at the end of the surah she is back to normal.

Not in this build yet: sounds and the Claude link (later plans).

If something goes wrong
-----------------------
Send me what you did and what happened (a screenshot or short video helps), and the log file engine.log:
right-click her icon → "Open Sahar folder" (or paste %LocalAppData%\Sahar into Explorer's address bar).
