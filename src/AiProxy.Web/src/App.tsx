import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AppShell } from './layout/AppShell';
import { navSections, settingsRoute, defaultRoutePath } from './routes/routes';
import { ModelDeploymentDetailPage } from './pages/config/ModelDeploymentDetailPage';

function App() {
  const allRoutes = [...navSections.flatMap((section) => section.routes), settingsRoute];

  return (
    <FluentProvider theme={webLightTheme}>
      <BrowserRouter>
        <AppShell>
          <Routes>
            <Route path="/" element={<Navigate to={defaultRoutePath} replace />} />
            {/* Not in the sidebar — reached by clicking a row on the Model Deployments list. */}
            <Route path="/config/model-deployments/:name" element={<ModelDeploymentDetailPage />} />
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
