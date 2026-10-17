import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { registrarServiceWorker } from './app/core/offline/sw';

bootstrapApplication(App, appConfig)
  .then(() => registrarServiceWorker())
  .catch((err) => console.error(err));
