import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

// Componente raiz: só mostra a página da rota atual.
@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  template: '<router-outlet />',
})
export class App {}
