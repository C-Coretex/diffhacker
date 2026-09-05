import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from '@/App';
import { RpcProvider } from '@/rpc/RpcProvider';
import '@/index.css';

const container = document.getElementById('root');
if (!container) {
  throw new Error('index.html is missing its #root element.');
}

createRoot(container).render(
  <StrictMode>
    <RpcProvider>
      <App />
    </RpcProvider>
  </StrictMode>,
);
