import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { Button, FluentProvider, Spinner, webLightTheme } from '@fluentui/react-components';
import { AppShell } from './layout/AppShell';
import { navSections, settingsRoute, defaultRoutePath } from './routes/routes';
import { ModelDetailPage } from './pages/config/ModelDetailPage';
import { AuthProvider, useAuth } from './auth/AuthContext';

function AuthenticatedApp() {
  const { session, loading, error, login } = useAuth();

  if (loading) {
    return <Spinner label="Loading session" />;
  }

  if (error) {
    return <div role="alert">{error}</div>;
  }

  if (!session) {
    return <Button appearance="primary" onClick={login}>Sign in</Button>;
  }

  const allRoutes = [...navSections.flatMap((section) => section.routes), settingsRoute];

  return (
    <AppShell>
      <Routes>
        <Route path="/" element={<Navigate to={defaultRoutePath} replace />} />
        <Route path="/config/models/:name" element={<ModelDetailPage />} />
        {allRoutes.map((route) => (
          <Route key={route.path} path={route.path} element={route.element} />
        ))}
      </Routes>
    </AppShell>
  );
}

function App() {
  return (
    <FluentProvider theme={webLightTheme}>
      <BrowserRouter>
        <AuthProvider>
          <AuthenticatedApp />
        </AuthProvider>
      </BrowserRouter>
    </FluentProvider>
  );
}

export default App;
