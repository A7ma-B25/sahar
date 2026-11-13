// STREAM C owns this page (0.14, mockup PSettingsToday); its words live in strings/mind-autonomy.js.
// Seam stub: the title only.
import { title } from '../controls.js';
import '../strings/mind-autonomy.js';

export function render({ tx }) {
  return [title(tx('pageToday'))];
}
