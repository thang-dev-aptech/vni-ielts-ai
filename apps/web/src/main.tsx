import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import '@vni/design-system/index.css';
import { App } from './App.js';

const root = document.getElementById('root');
if (!root) throw new Error('#root is missing from index.html');

createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
