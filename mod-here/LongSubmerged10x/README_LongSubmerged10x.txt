Long Submerged 10x+ v1.4.16

Paramètres utilisés :
- Oxygene long : applique au runtime sur le drain negatif de respiration
- Recharge surface : vanilla, aucune capacite Air/Oxygen/Atmosphere XLSX modifiee
- Discipline/fatigue sous l'eau : divisé par 15
- Batterie / Accumulators : x10
- EnergyUsage consommateurs hors ventilation/compresseurs : x0.1 dans les datasheets
- Mega Batterie runtime : case F10 active = batterie infinie, pompe incluse
- EnergyUsage recharge/production batterie : vanilla
- Deux derniers crans avant : vitesse/propulsion x8
- Deux derniers crans avant : carburant x8
- Vitesse max sous-marin joueur : 45 km/h
- Menu F10 : Batterie 1-100, Oxygene 1-100, SuperVitesse 1-20, Torpilles 1-10, Sonar 1-10, Blindage lourd x3, Super discrétion x3, Appeler renforts
- Slider Batterie : valeur legacy conservee, l'infini depend seulement de la case Mega Batterie
- Slider Oxygene : 1 = vanilla, 100 = profil environ 90 jours
- Slider SuperVitesse : 1 = vanilla, 8 = defaut actuel, 20 = maximum
- Slider Torpilles : 1 = vanilla, 10 = maximum
- Slider Sonar : 1 = vanilla, 3 = defaut actuel, 10 = maximum
- Blindage lourd : case desactivee par defaut, activable dans F10, degats joueur divises par 3 quand activee
- Super discrétion : case desactivee par defaut, bruit et detectabilite joueur divises par 3 quand activee
- Bouton Par defaut : restaure les reglages du profil actuel
- Mega torpilles : oui
- Mega torpilles degats : x10
- Mega torpilles effets visuels rayon explosion : x3
- Mega torpilles effets visuels intensite explosion : x3
- Mega torpilles guidage runtime : desactive pour stabilite surface/alarme
- Fiabilite parfaite torpilles : oui
- DudChance torpilles : 0
- Defaillance magnetique torpilles : 0
- Explosion magnetique prematuree torpilles : 0
- Menu en jeu : F10 pour activer/desactiver Mega Batterie, Mega Oxygene, SuperVitesse, Mega Torpilles, Mega Sonar, Blindage lourd, Super discrétion et Appeler renforts
- Bouton Appeler renforts : appelle des U-boats amis pres du joueur (10-16 km, minimum 8 km); avions/warships seulement si des spawners amis compatibles existent
- Eclairage interieur : rouge Alarm remplace visuellement par orange ambre, bleu SilentRun remplace visuellement par vert, gameplay inchange
- DLC Type IX officiel : lignes joueur Type IXA/IXC/IXC40 incluses si le DLC est installe
- Ventilation vanilla : oui
- Patch runtime : LongSubmerged10xPatch_1_4_16, air apres chargement, plafond vitesse, carburant rapide, torpilles, sonar, blindage lourd, super discretion, renforts, menu et stabilite surface/alarme

Installation :
1. Fermer UBOAT.
2. Générer avec --force --clear-cache.
3. Activer le mod dans le launcher.
4. Charger la sauvegarde ou démarrer une nouvelle carrière pour tester les changements d'air.

Notes :
- La jauge du jeu est une qualité d'air/atmosphère, pas un vrai compteur O2 détaillé.
- La lumiere d'alarme est orange ambre uniquement au rendu ; le mode Alarm et ses effets restent vanilla.
- La lumiere SilentRun est verte uniquement au rendu ; le mode SilentRun et ses effets restent vanilla.
- La ventilation reste vanilla par défaut pour éviter les bugs vus dans les essais précédents.
- Le patch runtime recalcule la respiration vanilla puis reduit seulement le drain negatif si Mega Oxygene est actif.
- Le profil air vise environ 90 jours d'immersion avec Mega Oxygene actif, sans toucher a la recharge surface.
- Mega Batterie cochee rend la batterie infinie ; decochee, la batterie revient vanilla.
- Blindage lourd est desactive par defaut ; coche dans F10, il divise les degats joueur par 3 sans rendre le sous-marin immortel.
- Migration settings v16 / v1.4.16 : les anciennes installations repassent Blindage lourd sur OFF une seule fois, puis tes choix F10 sont conserves.
- Super discrétion cochee divise le bruit et les detectabilites joueur par 3 sans supprimer les contacts ennemis.
- La profondeur d'ecrasement reste vanilla : depasser la limite critique peut toujours etre fatal.
- Les sliders F10 sont persistants et s'appliquent en partie avec un debounce ou Reappliquer maintenant.
- Les vitesses lentes et mi-vitesse restent vanilla ; seuls les deux crans rapides avant sont boostés vers 40/45 km/h.
- Les crans rapides consomment plus de carburant pour garder une autonomie logique.
- Les torpilles gardent leur vitesse/portee vanilla ; les degats, explosions, rates et le guidage verrouille sont geres en runtime.
- Mega Sonar augmente seulement la portee hydrophone ; x1 ou case decochee revient vanilla.
- Le guidage mega met les tirs verrouilles en cible cartésienne dynamique et force l'impact a courte distance.
- La fiabilite parfaite met DudChance, MagneticExplosionFail, MagneticExplosionOnArm et MagneticExplosionAfterArm a 0 quand Mega Torpilles est actif.
- Couper Mega Torpilles remet les torpilles sur les valeurs vanilla, car les XLSX torpilles ne sont pas ecrases.
- La recharge diesel reste vanilla pour éviter une recharge batterie instantanée.
- Le plafond de vitesse inclut les Type IX officiels du DLC Steam quand le DLC est installe.
- Compatible sauvegarde existante après fermeture complète puis relance du jeu.
- Si un autre mod touche l'air, mets Long Submerged 10x+ après lui dans l'ordre de chargement.

Compteurs de génération :
- Lignes batterie : 3
- Lignes EnergyUsage consommation : 6
- Lignes EnergyUsage recharge : 0
- Mega Batterie : case F10 active = batterie infinie, pompe incluse
- Menu F10 : sliders runtime bornes par profil, Blindage lourd x3, Super discrétion x3, Appeler renforts et bouton Par defaut
- SuperVitesse : runtime F10 reglable 1-20 sur les deux crans rapides avant
- Lignes vitesse sous-marin joueur : 0
- Mega torpilles : runtime F10 reglable 1-10, degats defaut x10, effets visuels bornes x3, aucune ligne torpille XLSX ecrasee
- Mega Sonar : runtime F10 reglable 1-10, defaut x3, applique aux portees hydrophone
- Blindage lourd : case F10 desactivee par defaut, activable manuellement, degats joueur divises par 3
- Super discrétion : case F10 desactivee par defaut, bruit et detectabilite joueur divisibles par 3
