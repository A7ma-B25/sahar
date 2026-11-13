// STREAM B owns this page (0.14, mockup PSettingsYou); its words live in strings/mind-memory.js.
// Seam stub: the title only.
import { title } from '../controls.js';
import '../strings/mind-memory.js';

export function render({ tx }) {
  return [title(tx('youTitle'))];
}
