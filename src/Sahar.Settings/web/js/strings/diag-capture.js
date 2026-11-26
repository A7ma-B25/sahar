// 0.15 T7 «ابعت سجلّي» (عن سحر) and T8 «اسمح بتصويرها» (الخصوصية): this stream's words (ar source, en pure mirror).
import { addStrings } from '../i18n.js';

addStrings({
  ar: {
    groupSupport: 'إذا في شي مو ماشي',
    sendLog: 'ابعت سجلّي',
    sendLogHint: 'ملف zip عالديسكتوب، بلا اسمك ولا شي عنك',
    sendLogDone: 'حطّيتلك سجلّي عالديسكتوب: {name}',
    sendLogDoneFolder: 'حطّيتلك سجلّي بمجلّدي: {name}',
    sendLogFailed: 'ما زبط السجل، جرّب كمان مرة.',
    groupCapture: 'التصوير',
    swCapture: 'اسمح بتصويرها',
    swCaptureHint: 'بطلع بتسجيل الشاشة واللقطات. وهي مطفية، الويندوز بيخبّيني.',
  },
  en: {
    groupSupport: 'If something is wrong',
    sendLog: 'Send my log',
    sendLogHint: 'A zip on your Desktop, without your name or anything about you',
    sendLogDone: 'I put my log on your Desktop: {name}',
    sendLogDoneFolder: 'I put my log in my folder: {name}',
    sendLogFailed: 'The log didn’t work. Try again.',
    groupCapture: 'Screen capture',
    swCapture: 'Allow screen capture',
    swCaptureHint: 'I show up in screen recordings and screenshots. Off, Windows hides me.',
  },
});
