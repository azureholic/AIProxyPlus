import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AppShell } from './layout/AppShell';
import { navSections, settingsRoute, defaultRoutePath } from './routes/routes';

function App() {
  const allRoutes = [...navSections.flatMap((section) => section.routes), settingsRoute];

  return (
    <FluentProvider theme={webLightTheme}>
      <BrowserRouter>
        <AppShell>
          <Routes>
            <Route path="/" element={<Navigate to={defaultRoutePath} replace />} />
            {allRoutes.map((route) => (
              <Route key={route.path} path={route.path} element={route.element} />
            ))}
          </Routes>
        </AppShell>
      </BrowserRouter>
    </FluentProvider>
  );
}

export default App;
