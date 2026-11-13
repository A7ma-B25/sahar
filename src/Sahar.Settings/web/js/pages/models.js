// STREAM A owns this page (0.14, mockup PSettingsModels); its words live in strings/mind-runtime.js.
// Seam stub: the title only.
import { title } from '../controls.js';
import '../strings/mind-runtime.js';

export function render({ tx }) {
  return [title(tx('pageModels'))];
}
