import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ConfirmacaoJanela } from './shared/confirmacao';

// Componente raiz: mostra a página da rota atual e a janela de confirmação (Confirmacao.pedir), que serve a todas.
@Component({
  imports: [RouterOutlet, ConfirmacaoJanela],
  selector: 'app-root',
  template: '<router-outlet /><app-confirmacao />',
})
export class App {}
