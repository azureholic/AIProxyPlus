import type { JSX } from 'react';
import {
  Board24Regular,
  BranchFork24Regular,
  Chat24Regular,
  CloudDatabaseRegular,
  DataUsage24Regular,
  Image24Regular,
  MicRecord24Regular,
  PeopleTeam24Regular,
  Settings24Regular,
} from '@fluentui/react-icons';
import { ModelsPage } from '../pages/config/ModelsPage';
import { EndpointsPage } from '../pages/config/EndpointsPage';
import { ApiRoutesPage } from '../pages/config/ApiRoutesPage';
import { ConsumersPage } from '../pages/config/ConsumersPage';
import { ChatPlaygroundPage } from '../pages/playground/ChatPlaygroundPage';
import { ImagePlaygroundPage } from '../pages/playground/ImagePlaygroundPage';
import { VoicePlaygroundPage } from '../pages/playground/VoicePlaygroundPage';
import { SpeechPlaygroundPage } from '../pages/playground/SpeechPlaygroundPage';
import { UsageDashboardPage } from '../pages/dashboard/UsageDashboardPage';
import { SettingsPage } from '../pages/SettingsPage';

export interface NavRoute {
  path: string;
  label: string;
  icon: JSX.Element;
  element: JSX.Element;
}

export interface NavSection {
  title: string;
  routes: NavRoute[];
}

/** Single source of truth for both the sidebar (AppShell) and the router (App) — see Phase 1 tasks. */
export const navSections: NavSection[] = [
  {
    title: 'Config',
    routes: [
      { path: '/config/models', label: 'Models', icon: <Board24Regular />, element: <ModelsPage /> },
      { path: '/config/endpoints', label: 'Endpoints', icon: <CloudDatabaseRegular />, element: <EndpointsPage /> },
      { path: '/config/api-routes', label: 'API Routes', icon: <BranchFork24Regular />, element: <ApiRoutesPage /> },
      { path: '/config/consumers', label: 'Consumers', icon: <PeopleTeam24Regular />, element: <ConsumersPage /> },
    ],
  },
  {
    title: 'Playground',
    routes: [
      { path: '/playground/chat', label: 'Chat', icon: <Chat24Regular />, element: <ChatPlaygroundPage /> },
      { path: '/playground/images', label: 'Images', icon: <Image24Regular />, element: <ImagePlaygroundPage /> },
      { path: '/playground/voice', label: 'Realtime Voice', icon: <MicRecord24Regular />, element: <VoicePlaygroundPage /> },
      { path: '/playground/speech', label: 'Speech', icon: <MicRecord24Regular />, element: <SpeechPlaygroundPage /> },
    ],
  },
  {
    title: 'Dashboard',
    routes: [{ path: '/dashboard/usage', label: 'Usage', icon: <DataUsage24Regular />, element: <UsageDashboardPage /> }],
  },
];

export const settingsRoute: NavRoute = { path: '/settings', label: 'Settings', icon: <Settings24Regular />, element: <SettingsPage /> };

export const defaultRoutePath = navSections[0].routes[0].path;
