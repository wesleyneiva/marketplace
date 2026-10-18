import { Directive, ElementRef, HostListener, inject } from '@angular/core';

// <dialog fecharAoClicarFora>: clicar no fundo escuro (fora da janela) fecha, como o X e o Esc.
// Só fecha se o clique COMEÇOU e TERMINOU fora: arrastar para selecionar um texto e soltar fora não fecha.
@Directive({ selector: 'dialog[fecharAoClicarFora]' })
export class FecharAoClicarFora {
  private readonly dialogo = inject<ElementRef<HTMLDialogElement>>(ElementRef).nativeElement;
  private comecouFora = false;

  private fora(e: MouseEvent): boolean {
    const r = this.dialogo.getBoundingClientRect();
    return e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom;
  }

  @HostListener('mousedown', ['$event'])
  protected aoApertar(e: MouseEvent): void {
    this.comecouFora = e.target === this.dialogo && this.fora(e);
  }

  @HostListener('click', ['$event'])
  protected aoClicar(e: MouseEvent): void {
    if (this.comecouFora && e.target === this.dialogo && this.fora(e)) this.dialogo.close();
    this.comecouFora = false;
  }
}
